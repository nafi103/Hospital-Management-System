using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HospitalManagementSystem.Services.Contracts
{
    // Deliberately minimal for Phase 1 - proves the spine (scrub -> call -> structured
    // parse -> persist -> review). Phase 2 replaces this with the full cited, streamed
    // case summary; this shape and JSON schema are what it builds on.
    public class CaseNoteDraft : IStructuredAiResponse
    {
        [JsonPropertyName("summary")]
        public string Summary { get; set; } = string.Empty;

        [JsonPropertyName("keyPoints")]
        public List<string> KeyPoints { get; set; } = new();

        public static string JsonSchema => """
        {
            "type": "object",
            "properties": {
                "summary": { "type": "string" },
                "keyPoints": { "type": "array", "items": { "type": "string" } }
            },
            "required": ["summary", "keyPoints"]
        }
        """;
    }
}
