using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace HospitalManagementSystem.Models
{
    public enum AiSuggestionType
    {
        CaseNoteDraft,
        CaseSummary,
        // A Bangla-language, plain-explanation instruction sheet for the patient, generated
        // from a specific prescription. TargetEntityId holds the PrescriptionId (the target
        // type varies by SuggestionType, same convention as the field's own comment below).
        PatientInstructions
    }

    public enum AiSuggestionVerdict
    {
        Pending,
        Accepted,
        Edited,
        Rejected
    }

    // The audit trail for every AI call in the system: what was asked, what came back,
    // what it cost, and what a human did with it. AI never writes to a clinical table
    // directly - it writes one of these, and a reviewer's Accept/Edit/Reject is the only
    // path from here into real data.
    [Index(nameof(PatientId))]
    [Index(nameof(CreatedAt))]
    public class AiSuggestion
    {
        [Key]
        public int Id { get; set; }

        public AiSuggestionType SuggestionType { get; set; }

        public int PatientId { get; set; }
        public Patient? Patient { get; set; }

        // Optional pointer to the entity this suggestion is about/would become
        // (e.g. a MedicalRecord or Prescription id). Not an FK: the target type
        // varies by SuggestionType, so it's resolved in code, not by the database.
        public int? TargetEntityId { get; set; }

        public string PayloadJson { get; set; } = string.Empty;

        // JSON array of the record IDs (medical records, vitals, etc.) the model was
        // given as context, so a later audit can reconstruct exactly what it saw.
        public string SourceRecordIds { get; set; } = "[]";

        public string ModelId { get; set; } = string.Empty;
        public string PromptVersion { get; set; } = string.Empty;

        public AiSuggestionVerdict Verdict { get; set; } = AiSuggestionVerdict.Pending;

        public int? ReviewedById { get; set; }
        [ForeignKey("ReviewedById")]
        public User? ReviewedBy { get; set; }
        public DateTime? ReviewedAt { get; set; }

        public string? EditedPayloadJson { get; set; }

        public int InputTokens { get; set; }
        public int OutputTokens { get; set; }
        public int CachedTokens { get; set; }
        public int LatencyMs { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}
