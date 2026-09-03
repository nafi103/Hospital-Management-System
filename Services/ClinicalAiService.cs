using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Google.GenAI;
using Google.GenAI.Types;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using HospitalManagementSystem.Models;
using HospitalManagementSystem.Services.Contracts;

namespace HospitalManagementSystem.Services
{
    public class ClinicalAiService : IClinicalAiService
    {
        private readonly ApplicationDbContext _context;
        private readonly Client _client;
        private readonly GeminiOptions _options;
        private readonly PhiScrubber _scrubber;
        private readonly ILogger<ClinicalAiService> _logger;

        public ClinicalAiService(ApplicationDbContext context, Client client, Microsoft.Extensions.Options.IOptions<GeminiOptions> options, PhiScrubber scrubber, ILogger<ClinicalAiService> logger)
        {
            _context = context;
            _client = client;
            _options = options.Value;
            _logger = logger;
            _scrubber = scrubber;
        }

        public async Task<AiSuggestion> GenerateCaseNoteDraftAsync(int patientId, int requestedByUserId, CancellationToken ct = default)
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

            var contextText = string.Join("\n\n", records.Select(r =>
                $"Record #{r.Id} ({r.RecordedAt:yyyy-MM-dd}):\n" +
                $"Chief complaint: {r.ChiefComplaint}\n" +
                $"Diagnosis: {r.Diagnosis}\n" +
                $"Treatment: {r.Treatment}"));

            var scrubbed = _scrubber.Scrub(contextText, patient);

            const string systemInstruction =
                "You are a clinical documentation assistant inside a hospital management system. " +
                "You will be given a patient's recent medical record entries. Produce a concise draft " +
                "case note: a 2-4 sentence summary and 3-6 key points a doctor should notice at a glance. " +
                "Only use information present in the records - never invent findings, medications, or dates. " +
                "Refer to the patient only by the pseudonymous identifier given, never assume a real name.";

            var userContent = $"Patient identifier: Patient-{patient.Uhid}\n\nMedical record history:\n{scrubbed.ScrubbedText}";

            var (draft, response, latencyMs) = await CallStructuredAsync<CaseNoteDraft>(
                systemInstruction, userContent, _options.ReasoningModel, ct);

            draft.Summary = _scrubber.Rehydrate(draft.Summary, scrubbed.Map);
            draft.KeyPoints = draft.KeyPoints.Select(p => _scrubber.Rehydrate(p, scrubbed.Map)).ToList();

            var suggestion = new AiSuggestion
            {
                SuggestionType = AiSuggestionType.CaseNoteDraft,
                PatientId = patientId,
                PayloadJson = JsonSerializer.Serialize(draft),
                SourceRecordIds = JsonSerializer.Serialize(records.Select(r => r.Id)),
                ModelId = response.ModelVersion ?? _options.ReasoningModel,
                PromptVersion = "case-note-draft-v1",
                Verdict = AiSuggestionVerdict.Pending,
                InputTokens = response.UsageMetadata?.PromptTokenCount ?? 0,
                OutputTokens = response.UsageMetadata?.CandidatesTokenCount ?? 0,
                CachedTokens = response.UsageMetadata?.CachedContentTokenCount ?? 0,
                LatencyMs = (int)latencyMs,
                CreatedAt = DateTime.UtcNow
            };

            _context.AiSuggestions.Add(suggestion);
            await _context.SaveChangesAsync(ct);

            return suggestion;
        }

        private async Task<(T Result, GenerateContentResponse Response, long LatencyMs)> CallStructuredAsync<T>(
            string systemInstruction, string userContent, string modelId, CancellationToken ct)
            where T : IStructuredAiResponse
        {
            var config = new GenerateContentConfig
            {
                SystemInstruction = new Content
                {
                    Parts = new List<Part> { new Part { Text = systemInstruction } }
                },
                ResponseMimeType = "application/json",
                ResponseJsonSchema = JsonNode.Parse(T.JsonSchema)
            };

            var stopwatch = Stopwatch.StartNew();
            GenerateContentResponse response;
            try
            {
                response = await _client.Models.GenerateContentAsync(
                    model: modelId, contents: userContent, config: config, cancellationToken: ct);
            }
            catch (HttpRequestException ex)
            {
                // Gemini's SDK doesn't always populate StatusCode (e.g. a 503 "high demand"
                // response surfaces with StatusCode null) - Unknown is the honest fallback,
                // and it still produces the right "try again" message for the user.
                var reason = ex.StatusCode switch
                {
                    HttpStatusCode.TooManyRequests => AiFailureReason.RateLimited,
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => AiFailureReason.Unauthorized,
                    _ => AiFailureReason.Unknown
                };
                _logger.LogError(ex, "Gemini request failed for model {Model} (status {StatusCode}): {Message}", modelId, ex.StatusCode, ex.Message);
                throw new ClinicalAiException(reason, "The AI service request failed.", ex);
            }
            catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogError(ex, "Gemini request timed out for model {Model}", modelId);
                throw new ClinicalAiException(AiFailureReason.Timeout, "The AI service did not respond in time.", ex);
            }
            catch (Exception ex) when (ex is not ClinicalAiException)
            {
                _logger.LogError(ex, "Unexpected failure calling Gemini model {Model}", modelId);
                throw new ClinicalAiException(AiFailureReason.Unknown, "Unexpected AI service failure.", ex);
            }
            stopwatch.Stop();

            var text = response.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new ClinicalAiException(AiFailureReason.InvalidResponse, "The AI service returned an empty response.");
            }

            T result;
            try
            {
                result = JsonSerializer.Deserialize<T>(text)
                    ?? throw new ClinicalAiException(AiFailureReason.InvalidResponse, "The AI service returned an unparseable response.");
            }
            catch (JsonException ex)
            {
                throw new ClinicalAiException(AiFailureReason.InvalidResponse, "The AI service returned a response that didn't match the expected format.", ex);
            }

            return (result, response, stopwatch.ElapsedMilliseconds);
        }
    }
}
