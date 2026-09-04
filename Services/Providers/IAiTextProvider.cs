using System.Collections.Generic;
using System.Threading;
using HospitalManagementSystem.Models;

namespace HospitalManagementSystem.Services.Providers
{
    // One chunk of a streamed text completion. Text is the incremental delta for this
    // chunk (never cumulative); the metadata fields are set only on the chunks that
    // actually carry them (e.g. Gemini repeats usage on every chunk, Anthropic splits
    // it across message_start/message_delta) - callers should keep the latest non-null
    // value seen, not require every field on every chunk.
    public record AiStreamChunk(string? Text, string? ModelId, int? InputTokens, int? OutputTokens, int? CachedTokens);

    // Implemented once per AI vendor (Gemini, Groq, Anthropic, ...). The API key and
    // model are passed per call, not injected via options, because both now come from
    // the admin-managed AiProviderSetting table and can change at runtime without an
    // app restart.
    public interface IAiTextProvider
    {
        AiProviderType Type { get; }

        IAsyncEnumerable<AiStreamChunk> StreamAsync(
            string apiKey, string modelId, string systemInstruction, string userContent, CancellationToken ct);
    }
}
