using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using HospitalManagementSystem.Hubs;
using HospitalManagementSystem.Models;
using HospitalManagementSystem.Services.Contracts;
using HospitalManagementSystem.Services.Providers;

namespace HospitalManagementSystem.Services
{
    public class ClinicalAiService : IClinicalAiService
    {
        private static readonly Regex CitationPattern = new(@"\[\[rec:(\d+)\]\]", RegexOptions.Compiled);

        private readonly ApplicationDbContext _context;
        private readonly PhiScrubber _scrubber;
        private readonly IMemoryCache _cache;
        private readonly IHubContext<AiStreamHub> _streamHub;
        private readonly AiProviderResolver _resolver;

        public ClinicalAiService(
            ApplicationDbContext context,
            PhiScrubber scrubber,
            IMemoryCache cache,
            IHubContext<AiStreamHub> streamHub,
            AiProviderResolver resolver)
        {
            _context = context;
            _scrubber = scrubber;
            _cache = cache;
            _streamHub = streamHub;
            _resolver = resolver;
        }

        // Fetches the patient's recent records and scrubs them for the model, caching the
        // scrubbed text so a doctor running several AI actions on the same patient in one
        // sitting doesn't re-fetch and re-scrub identical data each time. The cache key
        // bakes in the record count and max id, so a new/edited record simply misses
        // instead of needing explicit invalidation.
        private async Task<(Patient Patient, List<MedicalRecord> Records, ScrubResult Scrubbed)> BuildScrubbedPatientContextAsync(int patientId, CancellationToken ct)
        {
            var patient = await _context.Patients.FindAsync(new object?[] { patientId }, ct)
                ?? throw new ClinicalAiException(AiFailureReason.InvalidResponse, $"Patient {patientId} not found.");

            var records = await _context.MedicalRecords
                .Where(r => r.PatientId == patientId)
                .OrderByDescending(r => r.RecordedAt)
                .Take(10)
                .ToListAsync(ct);

            if (records.Count == 0)
            {
                throw new ClinicalAiException(AiFailureReason.InvalidResponse, "This patient has no medical records to summarize yet.");
            }

            var cacheKey = $"ai-ctx-{patientId}-{records.Count}-{records.Max(r => r.Id)}";
            if (!_cache.TryGetValue(cacheKey, out ScrubResult? scrubbed) || scrubbed is null)
            {
                var contextText = string.Join("\n\n", records.Select(r =>
                    $"Record #{r.Id} ({r.RecordedAt:yyyy-MM-dd}):\n" +
                    $"Chief complaint: {r.ChiefComplaint}\n" +
                    $"Diagnosis: {r.Diagnosis}\n" +
                    $"Treatment: {r.Treatment}"));

                scrubbed = _scrubber.Scrub(contextText, patient);
                _cache.Set(cacheKey, scrubbed, TimeSpan.FromMinutes(10));
            }

            return (patient, records, scrubbed);
        }

        // A pseudonym occurrence can straddle a chunk boundary, from any provider.
        // Rehydration must always run over the FULL raw buffer (never a truncated prefix
        // - Replace() needs every character of a match to even recognize it, so
        // truncating the input can silently skip a match that's actually complete).
        // What's withheld instead is a margin of trailing OUTPUT characters sized to the
        // longest pseudonym: an unresolved partial match can occupy at most that many
        // characters at the tail, so holding them back guarantees everything already
        // sent is final and can never be reshaped by a later chunk completing a match.
        private async Task<string> ConsumeAndStreamAsync(
            IAsyncEnumerable<AiStreamChunk> chunks,
            ScrubResult scrubbed,
            int maxPseudonymLength,
            Func<string, CancellationToken, Task> sendDelta,
            Action<AiStreamChunk> onMeta,
            CancellationToken ct)
        {
            var rawBuffer = new StringBuilder();
            var sentLength = 0;

            await foreach (var chunk in chunks.WithCancellation(ct))
            {
                onMeta(chunk);
                if (string.IsNullOrEmpty(chunk.Text))
                {
                    continue;
                }
                rawBuffer.Append(chunk.Text);

                var fullyRehydrated = _scrubber.Rehydrate(rawBuffer.ToString(), scrubbed.Map);
                var safeLength = Math.Min(fullyRehydrated.Length, Math.Max(sentLength, fullyRehydrated.Length - maxPseudonymLength));
                if (safeLength > sentLength)
                {
                    var delta = fullyRehydrated[sentLength..safeLength];
                    sentLength = safeLength;
                    await sendDelta(delta, ct);
                }
            }

            var finalText = _scrubber.Rehydrate(rawBuffer.ToString(), scrubbed.Map);
            // Flush whatever the withhold margin kept back at the very end of the stream -
            // otherwise the live view visibly falls short of the final saved text.
            if (finalText.Length > sentLength)
            {
                await sendDelta(finalText[sentLength..], CancellationToken.None);
            }
            return finalText;
        }

        public async Task<AiSuggestion> GenerateCaseSummaryAsync(int patientId, int requestedByUserId, string streamId, CancellationToken ct = default)
        {
            var groupName = $"AiStream_{streamId}";
            var group = _streamHub.Clients.Group(groupName);
            Task SendChunk(string text, CancellationToken token) => group.SendAsync("ReceiveChunk", text, token);

            var (patient, records, scrubbed) = await BuildScrubbedPatientContextAsync(patientId, ct);
            var validRecordIds = records.Select(r => r.Id).ToHashSet();
            var maxPseudonymLength = scrubbed.Map.Count > 0 ? scrubbed.Map.Keys.Max(k => k.Length) : 0;

            const string systemInstruction =
                "You are a clinical documentation assistant inside a hospital management system. " +
                "You will be given a patient's recent medical record entries, each labeled 'Record #<id>'. " +
                "Produce a scannable pre-visit summary a doctor can read in a few seconds, in this exact " +
                "format and nothing else: first, one headline sentence synthesizing why the patient is " +
                "being seen, wrapped in double asterisks like **this**, on its own line, with no citation " +
                "marker on it. Then, on their own lines, 3 to 6 bullet points, each starting with '- ', " +
                "each stating one self-contained clinical fact (a diagnosis, a treatment, or a current " +
                "status) - ordered chronologically, with the most recent/current status last. Immediately " +
                "after any bullet that draws on a specific record, insert a citation marker in the exact " +
                "form [[rec:<id>]] using that record's real id - never invent an id and never cite a " +
                "record you weren't given. Only use information present in the records - never invent " +
                "findings, medications, or dates. Refer to the patient only by the pseudonymous identifier " +
                "given, never assume a real name. Use no markdown beyond the headline's ** and the bullets' '- '.";

            var userContent = $"Patient identifier: Patient-{patient.Uhid}\n\nMedical record history:\n{scrubbed.ScrubbedText}";

            var providers = await _resolver.GetOrderedProvidersAsync(ct);
            if (providers.Count == 0)
            {
                throw new ClinicalAiException(AiFailureReason.Unauthorized, "No AI provider is configured. Ask an admin to add an API key.");
            }

            var stopwatch = Stopwatch.StartNew();
            string? finalText = null;
            string? resolvedModelId = null;
            int inputTokens = 0, outputTokens = 0, cachedTokens = 0;
            ClinicalAiException? lastFailure = null;

            for (var i = 0; i < providers.Count && finalText == null; i++)
            {
                var rp = providers[i];
                // One quick retry on the primary provider absorbs a short transient blip;
                // every other provider in the chain gets a single try before moving on.
                var maxAttempts = i == 0 ? 2 : 1;

                for (var attempt = 1; attempt <= maxAttempts && finalText == null; attempt++)
                {
                    if (i > 0 && attempt == 1)
                    {
                        await group.SendAsync("StreamRestarted", $"Switching to backup AI provider ({rp.Provider.Type})...", CancellationToken.None);
                    }
                    else if (attempt > 1)
                    {
                        await group.SendAsync("StreamRestarted", "Retrying...", CancellationToken.None);
                        await Task.Delay(TimeSpan.FromSeconds(3), ct);
                    }

                    try
                    {
                        resolvedModelId = rp.ModelId;
                        finalText = await ConsumeAndStreamAsync(
                            rp.Provider.StreamAsync(rp.ApiKey, rp.ModelId, systemInstruction, userContent, ct),
                            scrubbed, maxPseudonymLength, SendChunk,
                            chunk =>
                            {
                                if (chunk.ModelId != null) resolvedModelId = chunk.ModelId;
                                if (chunk.InputTokens.HasValue) inputTokens = chunk.InputTokens.Value;
                                if (chunk.OutputTokens.HasValue) outputTokens = chunk.OutputTokens.Value;
                                if (chunk.CachedTokens.HasValue) cachedTokens = chunk.CachedTokens.Value;
                            },
                            ct);
                    }
                    catch (ClinicalAiException ex)
                    {
                        lastFailure = ex;
                        if (ex.Reason != AiFailureReason.RateLimited)
                        {
                            break; // this provider is out - move to the next one, not just retry
                        }
                    }
                }
            }

            if (finalText == null)
            {
                await group.SendAsync("StreamError", "All configured AI providers are unavailable right now.", CancellationToken.None);
                throw lastFailure ?? new ClinicalAiException(AiFailureReason.Unknown, "All configured AI providers are unavailable right now.");
            }
            stopwatch.Stop();

            if (string.IsNullOrWhiteSpace(finalText))
            {
                await group.SendAsync("StreamError", "The AI service returned an empty response.", CancellationToken.None);
                throw new ClinicalAiException(AiFailureReason.InvalidResponse, "The AI service returned an empty response.");
            }

            var citedIds = new List<int>();
            var cleanedText = CitationPattern.Replace(finalText, match =>
            {
                var id = int.Parse(match.Groups[1].Value);
                if (!validRecordIds.Contains(id))
                {
                    return string.Empty; // hallucinated id - drop the marker, keep the sentence
                }
                if (!citedIds.Contains(id))
                {
                    citedIds.Add(id);
                }
                return match.Value;
            });

            var draft = new CaseSummaryDraft { NarrativeText = cleanedText.Trim(), CitedRecordIds = citedIds };

            var suggestion = new AiSuggestion
            {
                SuggestionType = AiSuggestionType.CaseSummary,
                PatientId = patientId,
                PayloadJson = JsonSerializer.Serialize(draft),
                SourceRecordIds = JsonSerializer.Serialize(records.Select(r => r.Id)),
                ModelId = resolvedModelId ?? "unknown",
                PromptVersion = "case-summary-v1",
                Verdict = AiSuggestionVerdict.Pending,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                CachedTokens = cachedTokens,
                LatencyMs = (int)stopwatch.ElapsedMilliseconds,
                CreatedAt = DateTime.UtcNow
            };

            _context.AiSuggestions.Add(suggestion);
            await _context.SaveChangesAsync(ct);

            await group.SendAsync("StreamComplete", suggestion.Id, CancellationToken.None);

            return suggestion;
        }
    }
}
