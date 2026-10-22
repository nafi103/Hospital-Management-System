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

        private async Task<(Patient Patient, List<MedicalRecord> Records, ScrubResult Scrubbed)> BuildScrubbedPatientContextAsync(int patientId, CancellationToken ct)
        {
            var patient = await _context.Patients
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == patientId, ct)
                ?? throw new ClinicalAiException(AiFailureReason.InvalidResponse, $"Patient {patientId} not found.");

            var records = await _context.MedicalRecords
                .AsNoTracking()
                .Where(r => r.PatientId == patientId)
                .OrderByDescending(r => r.RecordedAt)
                .Take(15) // Expanded window so chronic conditions (DM, HTN, IHD) aren't pushed out by minor visits
                .ToListAsync(ct);

            var allergies = await _context.PatientAllergies
                .AsNoTracking()
                .Where(a => a.PatientId == patientId)
                .OrderByDescending(a => a.CreatedAt)
                .ToListAsync(ct);

            var latestVital = await _context.PatientVitals
                .AsNoTracking()
                .Where(v => v.PatientId == patientId)
                .OrderByDescending(v => v.CreatedAt)
                .FirstOrDefaultAsync(ct);

            var prescriptions = await _context.Prescriptions
                .AsNoTracking()
                .Include(p => p.PrescriptionItems)
                    .ThenInclude(pi => pi.Medicine)
                .Where(p => p.PatientId == patientId)
                .OrderByDescending(p => p.CreatedAt)
                .Take(5)
                .ToListAsync(ct);

            var operations = await _context.Operations
                .AsNoTracking()
                .Where(o => o.PatientId == patientId)
                .OrderByDescending(o => o.OperationDatetime)
                .Take(10)
                .ToListAsync(ct);

            if (records.Count == 0 && allergies.Count == 0 && prescriptions.Count == 0 && operations.Count == 0 && latestVital == null)
            {
                throw new ClinicalAiException(AiFailureReason.InvalidResponse, "This patient has no clinical history, allergies, operations, or vitals to summarize yet.");
            }

            // Composite cache key across all 5 clinical domains
            var recordsTimestamp = records.Select(r => r.UpdatedAt.Ticks).DefaultIfEmpty(0).Max();
            var allergiesTimestamp = allergies.Select(a => a.CreatedAt.Ticks).DefaultIfEmpty(0).Max();
            var vitalsTimestamp = latestVital?.CreatedAt.Ticks ?? 0;
            var presTimestamp = prescriptions.Select(p => p.UpdatedAt.Ticks).DefaultIfEmpty(0).Max();
            var opsTimestamp = operations.Select(o => o.OperationDatetime.Ticks).DefaultIfEmpty(0).Max();

            var compositeKey = $"ai-ctx-v2-{patientId}-{records.Count}-{recordsTimestamp}-{allergies.Count}-{allergiesTimestamp}-{vitalsTimestamp}-{prescriptions.Count}-{presTimestamp}-{operations.Count}-{opsTimestamp}";

            if (!_cache.TryGetValue(compositeKey, out ScrubResult? scrubbed) || scrubbed is null)
            {
                var sb = new StringBuilder();

                // 0. Non-Identifying Biological Profile (Crucial for cardiovascular/metabolic & pediatric context)
                var ageYears = patient.DateOfBirth != default ? (int)((DateTime.UtcNow - patient.DateOfBirth).TotalDays / 365.25) : 0;
                sb.AppendLine($"Patient Demographics: Age {Math.Max(0, ageYears)} years, Gender: {patient.Gender}, Blood Group: {patient.BloodGroup ?? "Unknown"}");
                sb.AppendLine();

                // 1. Allergies & Contraindications (Highest Safety Priority)
                if (allergies.Count > 0)
                {
                    sb.AppendLine("CRITICAL - Patient Known Allergies:");
                    foreach (var a in allergies)
                    {
                        var genericPart = !string.IsNullOrEmpty(a.AllergenGenericName) ? $" (Generic: {a.AllergenGenericName})" : "";
                        var reactionPart = !string.IsNullOrEmpty(a.ReactionType) ? $" - Reaction: {a.ReactionType}" : "";
                        sb.AppendLine($"- {a.Substance}{genericPart} [Severity: {a.Severity}]{reactionPart}");
                    }
                    sb.AppendLine();
                }
                else
                {
                    sb.AppendLine("Patient Known Allergies: No known allergies recorded.\n");
                }

                // 2. Past Operations & Surgical History
                if (operations.Count > 0)
                {
                    sb.AppendLine("Surgical & Operation History:");
                    foreach (var op in operations)
                    {
                        var details = !string.IsNullOrWhiteSpace(op.PreparationChecklist) ? $": {op.PreparationChecklist}" : "";
                        sb.AppendLine($"- Operation in {op.OtRoom} ({op.OperationDatetime:yyyy-MM-dd}, Status: {op.Status}){details}");
                    }
                    sb.AppendLine();
                }

                // 3. Latest Vitals & NEWS2 Acuity
                if (latestVital != null)
                {
                    sb.AppendLine($"Latest Vital Signs ({latestVital.CreatedAt:yyyy-MM-dd HH:mm} UTC):");
                    sb.AppendLine($"- Blood Pressure: {latestVital.SystolicBp}/{latestVital.DiastolicBp} mmHg");
                    sb.AppendLine($"- Heart Rate: {latestVital.HeartRate} bpm, Respiratory Rate: {latestVital.RespiratoryRate}/min");
                    sb.AppendLine($"- SpO2: {latestVital.Spo2}%, Temperature: {latestVital.Temperature}°C, Supplemental O2: {(latestVital.OnSupplementalOxygen ? "Yes" : "No")}");
                    sb.AppendLine($"- Consciousness: {latestVital.Consciousness}, Blood Sugar: {(latestVital.BloodSugar.HasValue ? $"{latestVital.BloodSugar.Value} mmol/L" : "N/A")}, Weight: {(latestVital.WeightKg.HasValue ? $"{latestVital.WeightKg.Value} kg" : "N/A")}");
                    if (latestVital.TriagePriority.HasValue)
                    {
                        sb.AppendLine($"- NEWS2 Triage Priority: {latestVital.TriagePriority.Value}");
                    }
                    sb.AppendLine();
                }

                // 4. Active & Recent Prescriptions (Explicitly distinguishing Discontinued vs Active)
                if (prescriptions.Count > 0)
                {
                    sb.AppendLine("Active & Recent Medications:");
                    foreach (var p in prescriptions)
                    {
                        var statusStr = p.DiscontinuedById.HasValue
                            ? "DISCONTINUED"
                            : (p.Status == PrescriptionStatus.Dispensed ? "Active / Dispensed" : "Prescribed (Pending Dispense)");
                        var dateStr = (p.DispensedAt ?? p.CreatedAt).ToString("yyyy-MM-dd");
                        sb.AppendLine($"Prescription #{p.Id} ({dateStr}, Status: {statusStr}):");
                        if (!string.IsNullOrWhiteSpace(p.Diagnosis)) sb.AppendLine($"  Indication/Diagnosis: {p.Diagnosis}");
                        foreach (var item in p.PrescriptionItems)
                        {
                            var medName = item.Medicine?.Name ?? $"Medicine #{item.MedicineId}";
                            var genName = item.Medicine?.GenericName;
                            var genericStr = !string.IsNullOrWhiteSpace(genName) ? $" ({genName})" : "";
                            sb.AppendLine($"  - {medName}{genericStr}: {item.DoseDisplay}, {(item.DurationDays.HasValue ? $"{item.DurationDays} days" : "ongoing")}");
                        }
                    }
                    sb.AppendLine();
                }

                // 5. Longitudinal Medical Record History
                if (records.Count > 0)
                {
                    sb.AppendLine("Longitudinal Medical Records:");
                    sb.Append(string.Join("\n\n", records.Select(r =>
                        $"Record #{r.Id} ({r.RecordedAt:yyyy-MM-dd}):\n" +
                        $"Chief complaint: {r.ChiefComplaint}\n" +
                        $"Diagnosis: {r.Diagnosis}\n" +
                        $"Treatment/Notes: {r.Treatment}")));
                }

                scrubbed = _scrubber.Scrub(sb.ToString(), patient);
                _cache.Set(compositeKey, scrubbed, TimeSpan.FromMinutes(10));
            }

            return (patient, records, scrubbed);
        }

        private async Task<string> ConsumeAndStreamAsync(
            IAsyncEnumerable<AiStreamChunk> chunks,
            ScrubResult scrubbed,
            HashSet<int>? validRecordIds,
            int maxPseudonymLength,
            Func<string, CancellationToken, Task> sendDelta,
            Action<AiStreamChunk> onMeta,
            CancellationToken ct)
        {
            var rawBuffer = new StringBuilder();
            var sentLength = 0;
            var holdBackMargin = Math.Max(maxPseudonymLength, 32);

            string SanitizeAndRehydrate(string raw)
            {
                var rehydrated = _scrubber.Rehydrate(raw, scrubbed.Map);
                if (validRecordIds == null) return rehydrated;

                // Strip hallucinated record citations on the fly before they ever hit the WebSocket
                return CitationPattern.Replace(rehydrated, match =>
                {
                    return int.TryParse(match.Groups[1].Value, out var id) && validRecordIds.Contains(id)
                        ? match.Value
                        : string.Empty;
                });
            }

            await foreach (var chunk in chunks.WithCancellation(ct))
            {
                onMeta(chunk);
                if (string.IsNullOrEmpty(chunk.Text))
                {
                    continue;
                }
                rawBuffer.Append(chunk.Text);

                var processed = SanitizeAndRehydrate(rawBuffer.ToString());
                var safeLength = Math.Min(processed.Length, Math.Max(sentLength, processed.Length - holdBackMargin));

                // Stateful lookahead buffer: properly handles both single '[...]' pseudonyms
                // and double '[[rec:12]]' citation markers so neither splits across chunks.
                if (safeLength > sentLength)
                {
                    var candidate = processed[..safeLength];
                    var lastDoubleOpen = candidate.LastIndexOf("[[", StringComparison.Ordinal);
                    var lastSingleOpen = candidate.LastIndexOf('[');

                    if (lastDoubleOpen >= sentLength)
                    {
                        var closeDouble = candidate.IndexOf("]]", lastDoubleOpen + 2, StringComparison.Ordinal);
                        if (closeDouble == -1)
                        {
                            safeLength = lastDoubleOpen;
                        }
                    }
                    else if (lastSingleOpen >= sentLength)
                    {
                        var closeSingle = candidate.IndexOf(']', lastSingleOpen + 1);
                        if (closeSingle == -1)
                        {
                            safeLength = lastSingleOpen;
                        }
                    }
                }

                if (safeLength > sentLength)
                {
                    var delta = processed[sentLength..safeLength];
                    sentLength = safeLength;
                    await sendDelta(delta, ct);
                }
            }

            var finalText = SanitizeAndRehydrate(rawBuffer.ToString());
            if (finalText.Length > sentLength)
            {
                await sendDelta(finalText[sentLength..], CancellationToken.None);
            }
            return finalText;
        }

        private sealed record ProviderStreamResult(string FinalText, string ModelId, int InputTokens, int OutputTokens, int CachedTokens, int LatencyMs);

        private async Task<ProviderStreamResult> RunProviderStreamAsync(
            IClientProxy group,
            string systemInstruction,
            string userContent,
            ScrubResult scrubbed,
            HashSet<int>? validRecordIds,
            CancellationToken ct)
        {
            Task SendChunk(string text, CancellationToken token) => group.SendAsync("ReceiveChunk", text, token);
            var maxPseudonymLength = scrubbed.Map.Count > 0 ? scrubbed.Map.Keys.Max(k => k.Length) : 0;

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
                var maxAttempts = i == 0 ? 2 : 1;

                for (var attempt = 1; attempt <= maxAttempts && finalText == null; attempt++)
                {
                    if (i > 0 && attempt == 1)
                    {
                        await group.SendAsync("StreamRestarted", $"Switching to backup AI provider ({rp.Provider.Type})...", CancellationToken.None);
                    }
                    else if (attempt > 1)
                    {
                        await group.SendAsync("StreamRestarted", "Retrying primary AI provider...", CancellationToken.None);
                        await Task.Delay(TimeSpan.FromSeconds(1.5), ct);
                    }

                    try
                    {
                        resolvedModelId = rp.ModelId;
                        finalText = await ConsumeAndStreamAsync(
                            rp.Provider.StreamAsync(rp.ApiKey, rp.ModelId, systemInstruction, userContent, ct),
                            scrubbed, validRecordIds, maxPseudonymLength, SendChunk,
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
                        // If rate-limited or unauthorized, fail over immediately to the next provider
                        // rather than sleeping when another vendor is available.
                        if (ex.Reason == AiFailureReason.Unauthorized ||
                            ex.Reason == AiFailureReason.InvalidResponse ||
                            (ex.Reason == AiFailureReason.RateLimited && providers.Count > 1))
                        {
                            break;
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

            return new ProviderStreamResult(finalText, resolvedModelId ?? "unknown", inputTokens, outputTokens, cachedTokens, (int)stopwatch.ElapsedMilliseconds);
        }

        public async Task<AiSuggestion> GenerateCaseSummaryAsync(int patientId, int requestedByUserId, string streamId, CancellationToken ct = default)
        {
            var group = _streamHub.Clients.Group($"AiStream_{streamId}");

            var (patient, records, scrubbed) = await BuildScrubbedPatientContextAsync(patientId, ct);
            var validRecordIds = records.Select(r => r.Id).ToHashSet();

            const string systemInstruction =
                "You are a senior clinical decision-support assistant preparing a high-signal pre-consultation " +
                "briefing for a physician. Your goal is NOT to write a chronological diary of minor past visits; " +
                "instead, extract only clinically critical information that directly impacts today's diagnosis, " +
                "prescribing safety, and clinical management.\n\n" +
                "Produce this exact structure and nothing else:\n" +
                "1. HEADLINE: One single line wrapped in double asterisks (**like this**) summarizing the patient's " +
                "age/sex, major chronic comorbidities (e.g., Diabetes, Hypertension, Ischemic Heart Disease, CKD, " +
                "Asthma/COPD), past major surgeries, and severe allergies. Do not put citation markers in the headline.\n" +
                "2. CLINICAL BULLETS: 3 to 6 concise bullets, each starting with '- ', prioritized in this order:\n" +
                "   - Chronic Conditions & Major Comorbidities: Highlight lifelong or high-risk conditions (e.g., " +
                "diabetes, hypertension, cardiac disease, renal/hepatic impairment) and their control status. Ignore " +
                "resolved minor acute illnesses (like an old common cold) unless recurrent.\n" +
                "   - Surgical & Procedural History: Any past major operations or surgical interventions.\n" +
                "   - Allergies & Contraindications: Any documented drug or substance allergies and reaction severity.\n" +
                "   - Active Maintenance Medications: Ongoing long-term drugs the doctor must consider before prescribing.\n" +
                "   - Baseline Vitals & Red Flags: Highlight any abnormal latest vitals (elevated BP, high blood sugar, " +
                "hypoxia, fever, or elevated NEWS2 priority) or state that baseline vitals are stable.\n\n" +
                "STRICT RULES:\n" +
                "- Immediately after any bullet fact drawn from a specific 'Record #<id>', append [[rec:<id>]] using " +
                "that record's exact numeric ID. Never invent an ID.\n" +
                "- Only state facts explicitly present in the provided profile. Never invent conditions, surgeries, " +
                "allergies, or doses. Omit any category above that has no noteworthy findings.\n" +
                "- Use no markdown other than the headline's ** and each bullet's leading '- '.";

            var userContent = $"Clinical Profile:\n{scrubbed.ScrubbedText}";

            var result = await RunProviderStreamAsync(group, systemInstruction, userContent, scrubbed, validRecordIds, ct);

            var citedIds = new List<int>();
            var cleanedText = CitationPattern.Replace(result.FinalText, match =>
            {
                var id = int.Parse(match.Groups[1].Value);
                if (!validRecordIds.Contains(id))
                {
                    return string.Empty;
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
                ModelId = result.ModelId,
                PromptVersion = "case-summary-v2",
                Verdict = AiSuggestionVerdict.Pending,
                InputTokens = result.InputTokens,
                OutputTokens = result.OutputTokens,
                CachedTokens = result.CachedTokens,
                LatencyMs = result.LatencyMs,
                CreatedAt = DateTime.UtcNow
            };

            _context.AiSuggestions.Add(suggestion);
            await _context.SaveChangesAsync(ct);

            await group.SendAsync("StreamComplete", suggestion.Id, CancellationToken.None);

            return suggestion;
        }

        public async Task<AiSuggestion> GeneratePatientInstructionsAsync(int prescriptionId, int requestedByUserId, string streamId, CancellationToken ct = default)
        {
            var group = _streamHub.Clients.Group($"AiStream_{streamId}");

            var prescription = await _context.Prescriptions
                .AsNoTracking()
                .Include(p => p.Patient)
                .Include(p => p.PrescriptionItems)
                    .ThenInclude(i => i.Medicine)
                .FirstOrDefaultAsync(p => p.Id == prescriptionId, ct)
                ?? throw new ClinicalAiException(AiFailureReason.InvalidResponse, $"Prescription {prescriptionId} not found.");

            if (prescription.Patient == null)
            {
                throw new ClinicalAiException(AiFailureReason.InvalidResponse, "This prescription has no associated patient.");
            }
            if (prescription.PrescriptionItems.Count == 0)
            {
                throw new ClinicalAiException(AiFailureReason.InvalidResponse, "This prescription has no medicines to explain.");
            }

            var contextText =
                $"Diagnosis: {prescription.Diagnosis}\nChief complaints: {prescription.ChiefComplaints}\n\n" +
                "Prescribed medicines:\n" +
                string.Join("\n", prescription.PrescriptionItems.Select(i =>
                    $"- {i.Medicine!.Name} ({i.Medicine.GenericName}, {i.Medicine.Strength}): {i.DoseDisplay}, " +
                    $"{(i.DurationDays.HasValue ? $"{i.DurationDays} days" : "duration not specified")}, route {i.Route}." +
                    (string.IsNullOrWhiteSpace(i.Instructions) ? "" : $" Instructions: {i.Instructions}")));

            var scrubbed = _scrubber.Scrub(contextText, prescription.Patient);

            const string systemInstruction =
                "You are writing a plain-language patient instruction sheet in Bangla (Bengali script) for " +
                "a patient leaving a Bangladeshi hospital pharmacy. You will be given the diagnosis and the " +
                "exact medicines prescribed. Write entirely in Bangla, at a reading level a family member " +
                "with no medical training can follow. Produce exactly this format and nothing else: first, " +
                "one headline sentence in Bangla naming the overall purpose of the treatment, wrapped in " +
                "double asterisks like **this**, on its own line. Then, on their own lines, one bullet per " +
                "medicine starting with '- ', explaining in plain Bangla what it is for and how and when to " +
                "take it, referring to the medicine by the exact name given - never invent a medicine, dose, " +
                "or name not present in the input. After the medicine bullets, add 2 to 3 more bullets: " +
                "warning signs that mean the patient should return to the hospital, and the importance of " +
                "completing the full course. Use no markdown beyond the headline's ** and the bullets' '- '.";

            var userContent = scrubbed.ScrubbedText;

            var result = await RunProviderStreamAsync(group, systemInstruction, userContent, scrubbed, null, ct);

            var draft = new CaseSummaryDraft { NarrativeText = result.FinalText.Trim(), CitedRecordIds = new List<int>() };

            var suggestion = new AiSuggestion
            {
                SuggestionType = AiSuggestionType.PatientInstructions,
                PatientId = prescription.PatientId,
                TargetEntityId = prescription.Id,
                PayloadJson = JsonSerializer.Serialize(draft),
                SourceRecordIds = JsonSerializer.Serialize(new[] { prescription.Id }),
                ModelId = result.ModelId,
                PromptVersion = "patient-instructions-v2",
                Verdict = AiSuggestionVerdict.Pending,
                InputTokens = result.InputTokens,
                OutputTokens = result.OutputTokens,
                CachedTokens = result.CachedTokens,
                LatencyMs = result.LatencyMs,
                CreatedAt = DateTime.UtcNow
            };

            _context.AiSuggestions.Add(suggestion);
            await _context.SaveChangesAsync(ct);

            await group.SendAsync("StreamComplete", suggestion.Id, CancellationToken.None);

            return suggestion;
        }
    }
}
