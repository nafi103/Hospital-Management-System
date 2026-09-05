using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace HospitalManagementSystem.Models
{
    [Index(nameof(CreatedAt))]
    public class Prescription
    {
        [Key]
        public int Id { get; set; }

        public int PatientId { get; set; }
        public Patient? Patient { get; set; }

        public int DoctorId { get; set; }
        [ForeignKey("DoctorId")]
        public User? Doctor { get; set; }

        public string? Notes { get; set; }
        
        public string? ChiefComplaints { get; set; }
        public string? Diagnosis { get; set; }

        public PrescriptionStatus Status { get; set; }
        public bool IsBilled { get; set; } = false;

        // Populated only when PrescriptionSafetyChecker raised at least one warning and the
        // prescribing doctor explicitly acknowledged it - both null on an ordinary,
        // warning-free prescription. SafetyWarningsJson is a JSON array of the exact
        // warnings shown at the time, so the audit trail reflects what the doctor actually
        // saw, not a re-derived (and possibly since-changed) check result.
        public string? SafetyOverrideReason { get; set; }
        public string? SafetyWarningsJson { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public List<PrescriptionItem> PrescriptionItems { get; set; } = new List<PrescriptionItem>();
    }
}
