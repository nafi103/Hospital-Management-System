using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace HospitalManagementSystem.Models
{
    // Composite, not (PatientId) + (CreatedAt) separately: every real query is
    // "this patient's vitals, most recent first" (dashboard, NEWS2 trend), which a
    // single (PatientId, CreatedAt DESC) index serves as a plain index scan. It also
    // covers patient-only lookups as a prefix, so no separate PatientId index is needed.
    [Index(nameof(PatientId), nameof(CreatedAt), IsDescending = new[] { false, true })]
    public class PatientVital
    {
        [Key]
        public int Id { get; set; }

        public int PatientId { get; set; }
        public Patient Patient { get; set; }

        public int? AppointmentId { get; set; }
        public Appointment Appointment { get; set; }

        public int SystolicBp { get; set; }
        public int DiastolicBp { get; set; }
        public int HeartRate { get; set; }
        public decimal Spo2 { get; set; }
        public decimal Temperature { get; set; }

        [Obsolete("Superseded by RespiratoryRate + OnSupplementalOxygen. Retained for existing records; do not write new values.")]
        public bool RespiratoryDistress { get; set; }

        public decimal? BloodSugar { get; set; }

        // NEWS2 scoring inputs.
        public int RespiratoryRate { get; set; }
        public bool OnSupplementalOxygen { get; set; }
        public ConsciousnessLevel Consciousness { get; set; } = ConsciousnessLevel.Alert;

        public TriagePriority? TriagePriority { get; set; }

        public int RecordedById { get; set; }
        [ForeignKey("RecordedById")]
        public User RecordedBy { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}
