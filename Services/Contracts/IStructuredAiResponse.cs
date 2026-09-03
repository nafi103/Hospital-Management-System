namespace HospitalManagementSystem.Services.Contracts
{
    // Lets the generic Gemini call helper in ClinicalAiService fetch the right JSON
    // schema for whatever DTO type it's asked to deserialize into.
    public interface IStructuredAiResponse
    {
        static abstract string JsonSchema { get; }
    }
}
