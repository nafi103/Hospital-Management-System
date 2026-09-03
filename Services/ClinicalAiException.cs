using System;

namespace HospitalManagementSystem.Services
{
    public enum AiFailureReason
    {
        RateLimited,
        Unauthorized,
        Timeout,
        InvalidResponse,
        Unknown
    }

    // Thrown by IClinicalAiService on any failure. Callers catch this one type and
    // branch on Reason for a user-facing message - the underlying page's real data
    // stays on screen either way, it just doesn't get an AI suggestion this time.
    public class ClinicalAiException : Exception
    {
        public AiFailureReason Reason { get; }

        public ClinicalAiException(AiFailureReason reason, string message, Exception? inner = null)
            : base(message, inner)
        {
            Reason = reason;
        }
    }
}
