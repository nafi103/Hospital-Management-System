using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HospitalManagementSystem.Models
{
    // No standalone PatientId index: the unique (PatientId, SubstanceNormalized) index
    // configured in ApplicationDbContext.OnModelCreating covers patient-only lookups as
    // a prefix, so a second single-column index would be pure duplication.
    public class PatientAllergy
    {
        [Key]
        public int Id { get; set; }

        public int PatientId { get; set; }
        public Patient? Patient { get; set; }

        [Required]
        [StringLength(150)]
        public string Substance { get; set; } = string.Empty;

        // Canonical generic name when this allergy is to a known formulary drug (matches
        // Medicine.GenericName exactly), so the P4 prescription safety check can compare
        // against it directly instead of fuzzy-matching free text. Null for non-drug
        // allergies (peanuts, latex, ...) or when the substance wasn't in the formulary.
        [StringLength(100)]
        public string? AllergenGenericName { get; set; }

        public string? ReactionType { get; set; }

        public AllergySeverity Severity { get; set; }

        public int RecordedById { get; set; }
        [ForeignKey("RecordedById")]
        public User? RecordedBy { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}
