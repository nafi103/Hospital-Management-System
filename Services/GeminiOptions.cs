namespace HospitalManagementSystem.Services
{
    public class GeminiOptions
    {
        public string ApiKey { get; set; } = string.Empty;

        // Higher-reasoning model for clinical synthesis (case summaries, safety checks).
        // Defaults to Flash, not a Pro-tier model: free-tier API keys currently get zero
        // quota on Pro models (confirmed via a direct quota-exceeded response, not a
        // transient error). Point this at a Pro model once the account has billing enabled.
        public string ReasoningModel { get; set; } = "gemini-3.8-flash";

        // Cheaper/faster model for short, low-stakes generations (triage narration).
        public string FastModel { get; set; } = "gemini-3.8-flash";
    }
}
