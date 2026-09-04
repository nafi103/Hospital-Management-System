namespace HospitalManagementSystem.Services
{
    public class GroqOptions
    {
        public string ApiKey { get; set; } = string.Empty;

        // Free-tier Groq model used only as a fallback when Gemini is unavailable.
        public string Model { get; set; } = "openai/gpt-oss-20b";
    }
}
