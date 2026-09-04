using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;

namespace HospitalManagementSystem.Services
{
    public readonly record struct GroqStreamDelta(string? Text, int PromptTokens, int CompletionTokens);

    // Groq exposes an OpenAI-compatible Chat Completions API, so this is a small,
    // dependency-free SSE client rather than a full SDK - the only surface used here is
    // a system+user message pair with streaming text deltas and trailing usage. The API
    // key and model are passed per call (from the admin-managed AiProviderSetting row
    // via AiProviderResolver), not injected from static config.
    public class GroqClient
    {
        private readonly HttpClient _http;

        public GroqClient(HttpClient http)
        {
            _http = http;
            _http.BaseAddress = new System.Uri("https://api.groq.com/openai/v1/");
        }

        public async IAsyncEnumerable<GroqStreamDelta> StreamChatAsync(
            string apiKey, string model, string systemInstruction, string userContent,
            [EnumeratorCancellation] CancellationToken ct)
        {
            var requestBody = new
            {
                model,
                stream = true,
                stream_options = new { include_usage = true },
                messages = new object[]
                {
                    new { role = "system", content = systemInstruction },
                    new { role = "user", content = userContent }
                }
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
            {
                Content = JsonContent.Create(requestBody)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                throw new HttpRequestException($"Groq request failed: {body}", null, response.StatusCode);
            }

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);
            string? line;
            while ((line = await reader.ReadLineAsync(ct)) != null)
            {
                if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data: "))
                {
                    continue;
                }

                var payload = line["data: ".Length..];
                if (payload == "[DONE]")
                {
                    yield break;
                }

                using var doc = JsonDocument.Parse(payload);
                var root = doc.RootElement;

                string? delta = null;
                if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                {
                    var choice = choices[0];
                    if (choice.TryGetProperty("delta", out var deltaEl) &&
                        deltaEl.TryGetProperty("content", out var contentEl) &&
                        contentEl.ValueKind == JsonValueKind.String)
                    {
                        delta = contentEl.GetString();
                    }
                }

                var promptTokens = 0;
                var completionTokens = 0;
                if (root.TryGetProperty("usage", out var usageEl) && usageEl.ValueKind == JsonValueKind.Object)
                {
                    promptTokens = usageEl.TryGetProperty("prompt_tokens", out var pt) ? pt.GetInt32() : 0;
                    completionTokens = usageEl.TryGetProperty("completion_tokens", out var ct2) ? ct2.GetInt32() : 0;
                }

                yield return new GroqStreamDelta(delta, promptTokens, completionTokens);
            }
        }
    }
}
