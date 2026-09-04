using System.Threading;
using System.Threading.Tasks;
using HospitalManagementSystem.Models;

namespace HospitalManagementSystem.Services
{
    // Every AI capability in the system is a method on this interface. Each one builds
    // context, scrubs it, calls the model, and persists an AiSuggestion row - never
    // writes to a clinical table directly. Throws ClinicalAiException on failure;
    // callers never see a raw SDK exception.
    public interface IClinicalAiService
    {
        // Streams the narrative to group "AiStream_{streamId}" on AiStreamHub as it
        // generates (event "ReceiveChunk"), then persists the finished, cited result
        // as an AiSuggestion and returns it. The caller signals stream end/failure to
        // the same group ("StreamComplete" / "StreamError") - see ClinicalAiService.
        Task<AiSuggestion> GenerateCaseSummaryAsync(int patientId, int requestedByUserId, string streamId, CancellationToken ct = default);
    }
}
