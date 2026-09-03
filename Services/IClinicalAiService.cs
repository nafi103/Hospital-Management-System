using System.Threading;
using System.Threading.Tasks;
using HospitalManagementSystem.Models;

namespace HospitalManagementSystem.Services
{
    // Every AI capability in the system is a method on this interface. Each one builds
    // context, scrubs it, calls the model for structured output, and persists an
    // AiSuggestion row - never writes to a clinical table directly. Throws
    // ClinicalAiException on failure; callers never see a raw SDK exception.
    public interface IClinicalAiService
    {
        Task<AiSuggestion> GenerateCaseNoteDraftAsync(int patientId, int requestedByUserId, CancellationToken ct = default);
    }
}
