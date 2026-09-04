using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Logging;
using HospitalManagementSystem.Models;
using AnthropicRole = Anthropic.Models.Messages.Role;

namespace HospitalManagementSystem.Services.Providers
{
    public class AnthropicTextProvider : IAiTextProvider
    {
        // AnthropicClient wraps its own HttpClient, so instances are cached per API key
        // rather than rebuilt on every call - same pattern as the Gemini provider.
        private static readonly ConcurrentDictionary<string, AnthropicClient> _clients = new();

        private readonly ILogger<AnthropicTextProvider> _logger;

        public AnthropicTextProvider(ILogger<AnthropicTextProvider> logger)
        {
            _logger = logger;
        }

        public AiProviderType Type => AiProviderType.Anthropic;

        private static AnthropicClient GetClient(string apiKey) =>
            _clients.GetOrAdd(apiKey, key => new AnthropicClient { ApiKey = key });

        public async IAsyncEnumerable<AiStreamChunk> StreamAsync(
            string apiKey, string modelId, string systemInstruction, string userContent,
            [EnumeratorCancellation] CancellationToken ct)
        {
            var client = GetClient(apiKey);
            var parameters = new MessageCreateParams
            {
                Model = modelId,
                MaxTokens = 64000,
                System = systemInstruction,
                Messages = [new() { Role = AnthropicRole.User, Content = userContent }]
            };

            IAsyncEnumerator<RawMessageStreamEvent> enumerator;
            try
            {
                enumerator = client.Messages.CreateStreaming(parameters).GetAsyncEnumerator(ct);
            }
            catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogError(ex, "Anthropic streaming request timed out for model {Model}", modelId);
                throw new ClinicalAiException(AiFailureReason.Timeout, "The AI service did not respond in time.", ex);
            }
            catch (Exception ex) when (ex is not ClinicalAiException)
            {
                throw MapException(ex, modelId);
            }

            await using (enumerator)
            {
                while (true)
                {
                    RawMessageStreamEvent streamEvent;
                    try
                    {
                        if (!await enumerator.MoveNextAsync())
                        {
                            break;
                        }
                        streamEvent = enumerator.Current;
                    }
                    catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
                    {
                        _logger.LogError(ex, "Anthropic streaming request timed out for model {Model}", modelId);
                        throw new ClinicalAiException(AiFailureReason.Timeout, "The AI service did not respond in time.", ex);
                    }
                    catch (Exception ex) when (ex is not ClinicalAiException)
                    {
                        throw MapException(ex, modelId);
                    }

                    if (streamEvent.TryPickStart(out var startEvent))
                    {
                        yield return new AiStreamChunk(
                            null, startEvent.Message.Model, (int)startEvent.Message.Usage.InputTokens, null, null);
                    }
                    else if (streamEvent.TryPickContentBlockDelta(out var blockDelta) &&
                             blockDelta.Delta.TryPickText(out var textDelta))
                    {
                        yield return new AiStreamChunk(textDelta.Text, null, null, null, null);
                    }
                    else if (streamEvent.TryPickDelta(out var messageDelta))
                    {
                        yield return new AiStreamChunk(
                            null, null, null, (int)messageDelta.Usage.OutputTokens, null);
                    }
                }
            }
        }

        private ClinicalAiException MapException(Exception ex, string modelId)
        {
            var reason = ex switch
            {
                AnthropicRateLimitException => AiFailureReason.RateLimited,
                AnthropicUnauthorizedException => AiFailureReason.Unauthorized,
                AnthropicForbiddenException => AiFailureReason.Unauthorized,
                Anthropic5xxException => AiFailureReason.RateLimited,
                _ => AiFailureReason.Unknown
            };
            _logger.LogError(ex, "Anthropic streaming request failed for model {Model}: {Message}", modelId, ex.Message);
            return new ClinicalAiException(reason, "The AI service request failed.", ex);
        }
    }
}
