using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HospitalManagementSystem.Services.Contracts
{
    // Built from a streamed, free-text model response, not a JSON-schema call - the
    // service assembles this itself after the stream ends, so it's a plain DTO, not
    // an IStructuredAiResponse. NarrativeText keeps its "[[rec:ID]]" citation markers
    // (only the ones that survived validation against the actual source records);
    // _SuggestionCard.cshtml renders each marker as a link to that record.
    public class CaseSummaryDraft
    {
        [JsonPropertyName("narrativeText")]
        public string NarrativeText { get; set; } = string.Empty;

        [JsonPropertyName("citedRecordIds")]
        public List<int> CitedRecordIds { get; set; } = new();
    }
}
