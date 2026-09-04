using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace HospitalManagementSystem.Models
{
    public enum AiProviderType
    {
        Gemini,
        Groq,
        Anthropic
    }

    // Admin-managed AI provider configuration. Priority (lower = tried first) is what
    // ClinicalAiService's fallback chain walks, in place of the old hardcoded
    // Gemini-then-Groq order - buying a new provider's key and setting it to Priority 0
    // is the whole "add Claude" story. EncryptedApiKey is never returned to a view;
    // the admin UI only ever shows "configured" / "not configured".
    [Index(nameof(Provider), IsUnique = true)]
    public class AiProviderSetting
    {
        [Key]
        public int Id { get; set; }

        public AiProviderType Provider { get; set; }

        public string EncryptedApiKey { get; set; } = string.Empty;

        public string ModelId { get; set; } = string.Empty;

        public bool IsEnabled { get; set; } = true;

        public int Priority { get; set; }

        public int? UpdatedById { get; set; }
        [ForeignKey("UpdatedById")]
        public User? UpdatedBy { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
