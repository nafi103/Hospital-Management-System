using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.Extensions.Logging;
using HospitalManagementSystem.Models;

namespace HospitalManagementSystem.Services.Providers
{
    public class GroqTextProvider : IAiTextProvider
    {
        private readonly GroqClient _client;
        private readonly ILogger<GroqTextProvider> _logger;

        public GroqTextProvider(GroqClient client, ILogger<GroqTextProvider> logger)
        {
            _client = client;
            _logger = logger;
        }

        public AiProviderType Type => AiProviderType.Groq;

        public async IAsyncEnumerable<AiStreamChunk> StreamAsync(
            string apiKey, string modelId, string systemInstruction, string userContent,
            [EnumeratorCancellation] CancellationToken ct)
        {
            var enumerator = _client.StreamChatAsync(apiKey, modelId, systemInstruction, userContent, ct).GetAsyncEnumerator(ct);
            await using (enumerator)
            {
                while (true)
                {
                    GroqStreamDelta chunk;
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
                        var reason = ex.StatusCode switch
                        {
                            HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable => AiFailureReason.RateLimited,
                            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => AiFailureReason.Unauthorized,
                            _ => AiFailureReason.Unknown
                        };
                        _logger.LogError(ex, "Groq streaming request failed for model {Model} (status {StatusCode}): {Message}", modelId, ex.StatusCode, ex.Message);
                        throw new ClinicalAiException(reason, "The backup AI service request failed.", ex);
                    }
                    catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
                    {
                        _logger.LogError(ex, "Groq streaming request timed out for model {Model}", modelId);
                        throw new ClinicalAiException(AiFailureReason.Timeout, "The backup AI service did not respond in time.", ex);
                    }
                    catch (Exception ex) when (ex is not ClinicalAiException)
                    {
                        _logger.LogError(ex, "Unexpected failure streaming from Groq model {Model}", modelId);
                        throw new ClinicalAiException(AiFailureReason.Unknown, "Unexpected backup AI service failure.", ex);
                    }

                    var hasUsage = chunk.PromptTokens > 0 || chunk.CompletionTokens > 0;
                    yield return new AiStreamChunk(
                        chunk.Text,
                        modelId,
                        hasUsage ? chunk.PromptTokens : null,
                        hasUsage ? chunk.CompletionTokens : null,
                        null);
                }
            }
        }
    }
}
