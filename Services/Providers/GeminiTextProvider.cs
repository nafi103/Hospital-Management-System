using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using Google.GenAI;
using Google.GenAI.Types;
using Microsoft.Extensions.Logging;
using HospitalManagementSystem.Models;

namespace HospitalManagementSystem.Services.Providers
{
    public class GeminiTextProvider : IAiTextProvider
    {
        // Google.GenAI.Client owns its own HttpClient, so instances are cached per API
        // key rather than rebuilt on every call. A rotated/removed key simply stops
        // being looked up - the stale entry is harmless, unused garbage.
        private static readonly ConcurrentDictionary<string, Client> _clients = new();

        private readonly ILogger<GeminiTextProvider> _logger;

        public GeminiTextProvider(ILogger<GeminiTextProvider> logger)
        {
            _logger = logger;
        }

        public AiProviderType Type => AiProviderType.Gemini;

        private static Client GetClient(string apiKey) =>
            _clients.GetOrAdd(apiKey, key => new Client(apiKey: key));

        public async IAsyncEnumerable<AiStreamChunk> StreamAsync(
            string apiKey, string modelId, string systemInstruction, string userContent,
            [EnumeratorCancellation] CancellationToken ct)
        {
            var client = GetClient(apiKey);
            var config = new GenerateContentConfig
            {
                SystemInstruction = new Content
                {
                    Parts = new List<Part> { new Part { Text = systemInstruction } }
                }
            };

            IAsyncEnumerator<GenerateContentResponse> enumerator;
            try
            {
                enumerator = client.Models.GenerateContentStreamAsync(
                    model: modelId, contents: userContent, config: config, cancellationToken: ct).GetAsyncEnumerator(ct);
            }
            catch (HttpRequestException ex)
            {
                throw MapException(ex, modelId);
            }

            await using (enumerator)
            {
                while (true)
                {
                    GenerateContentResponse chunk;
                    try
                    {
                        if (!await enumerator.MoveNextAsync())
                        {
                            break;
                        }
                        chunk = enumerator.Current;
                    }
                    catch (HttpRequestException ex)
                    {
                        throw MapException(ex, modelId);
                    }
                    catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
                    {
                        _logger.LogError(ex, "Gemini streaming request timed out for model {Model}", modelId);
                        throw new ClinicalAiException(AiFailureReason.Timeout, "The AI service did not respond in time.", ex);
                    }
                    catch (Exception ex) when (ex is not ClinicalAiException)
                    {
                        _logger.LogError(ex, "Unexpected failure streaming from Gemini model {Model}", modelId);
                        throw new ClinicalAiException(AiFailureReason.Unknown, "Unexpected AI service failure.", ex);
                    }

                    yield return new AiStreamChunk(
                        chunk.Text,
                        chunk.ModelVersion,
                        chunk.UsageMetadata?.PromptTokenCount,
                        chunk.UsageMetadata?.CandidatesTokenCount,
                        chunk.UsageMetadata?.CachedContentTokenCount);
                }
            }
        }

        // Google.GenAI.ServerError/ClientError both derive from HttpRequestException but
        // redeclare their own "new int StatusCode" that hides the base class's nullable
        // HttpStatusCode? property - a plain `ex.StatusCode` in a `catch (HttpRequestException)`
        // block binds to the hidden base member and is always null for these, silently
        // collapsing every Gemini error (rate limits, auth failures, 503s) to Unknown.
        // Confirmed by reflecting the installed SDK assembly directly, not assumed.
        private ClinicalAiException MapException(HttpRequestException ex, string modelId)
        {
            var statusCode = ex switch
            {
                Google.GenAI.ServerError se => (HttpStatusCode)se.StatusCode,
                Google.GenAI.ClientError ce => (HttpStatusCode)ce.StatusCode,
                _ => ex.StatusCode
            };
            var reason = statusCode switch
            {
                HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable => AiFailureReason.RateLimited,
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => AiFailureReason.Unauthorized,
                _ => AiFailureReason.Unknown
            };
            _logger.LogError(ex, "Gemini streaming request failed for model {Model} (status {StatusCode}): {Message}", modelId, statusCode, ex.Message);
            return new ClinicalAiException(reason, "The AI service request failed.", ex);
        }
    }
}
