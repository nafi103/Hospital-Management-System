using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HospitalManagementSystem.Models;
using HospitalManagementSystem.Services.Providers;

namespace HospitalManagementSystem.Tests.Helpers
{
    public class TestAiTextProvider : IAiTextProvider
    {
        public AiProviderType Type { get; set; } = AiProviderType.Groq;

        public Func<string, string, CancellationToken, Task<(bool Success, string? ErrorMessage)>>? PingFunc { get; set; }

        public Func<string, string, string, string, CancellationToken, IAsyncEnumerable<AiStreamChunk>>? StreamFunc { get; set; }

        public Task<(bool Success, string? ErrorMessage)> PingAsync(string apiKey, string modelId, CancellationToken ct)
        {
            if (PingFunc != null)
            {
                return PingFunc(apiKey, modelId, ct);
            }
            return Task.FromResult((true, (string?)null));
        }

        public async IAsyncEnumerable<AiStreamChunk> StreamAsync(
            string apiKey, string modelId, string systemInstruction, string userContent,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            if (StreamFunc != null)
            {
                await foreach (var chunk in StreamFunc(apiKey, modelId, systemInstruction, userContent, ct))
                {
                    yield return chunk;
                }
            }
            else
            {
                yield return new AiStreamChunk("Default test AI response", modelId, 10, 5, 0);
            }
        }
    }
}
