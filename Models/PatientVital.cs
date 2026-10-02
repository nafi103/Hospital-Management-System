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

        [Required]
        [Range(40, 260, ErrorMessage = "Systolic BP must be between 40 and 260 mmHg.")]
        public int SystolicBp { get; set; }

        [Required]
        [Range(20, 180, ErrorMessage = "Diastolic BP must be between 20 and 180 mmHg.")]
        public int DiastolicBp { get; set; }

        [Required]
        [Range(25, 250, ErrorMessage = "Heart Rate must be between 25 and 250 bpm.")]
        public int HeartRate { get; set; }

        [Required]
        [Range(50.0, 100.0, ErrorMessage = "SpO2 must be between 50% and 100%.")]
        public decimal Spo2 { get; set; }

        [Required]
        [Range(30.0, 45.0, ErrorMessage = "Temperature must be between 30.0°C and 45.0°C.")]
        public decimal Temperature { get; set; }

        [Obsolete("Superseded by RespiratoryRate + OnSupplementalOxygen. Retained for existing records; do not write new values.")]
        public bool RespiratoryDistress { get; set; }

        [Range(0.5, 50.0, ErrorMessage = "Blood Sugar must be between 0.5 and 50.0 mmol/L.")]
        public decimal? BloodSugar { get; set; }

        [Range(0.5, 350.0, ErrorMessage = "Weight must be between 0.5 and 350.0 kg.")]
        public decimal? WeightKg { get; set; }

        // NEWS2 scoring inputs.
        [Required]
        [Range(4, 60, ErrorMessage = "Respiratory Rate must be between 4 and 60 breaths/min.")]
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
