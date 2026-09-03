using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace HospitalManagementSystem.Models
{
    [Index(nameof(PatientId))]
    [Index(nameof(RecordedAt))]
    public class MedicalRecord
    {
        [Key]
        public int Id { get; set; }

        public int PatientId { get; set; }
        public Patient? Patient { get; set; }

        public int DoctorId { get; set; }
        [ForeignKey("DoctorId")]
        public User? Doctor { get; set; }

        public int? AppointmentId { get; set; }
        public Appointment? Appointment { get; set; }

        public string? ChiefComplaint { get; set; }
        public string Diagnosis { get; set; } = string.Empty;
        public string Treatment { get; set; } = string.Empty;

        public DateTime RecordedAt { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
