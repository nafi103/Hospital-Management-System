using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace HospitalManagementSystem.Models
{
    [Index(nameof(PatientId))]
    public class PatientAllergy
    {
        [Key]
        public int Id { get; set; }

        public int PatientId { get; set; }
        public Patient? Patient { get; set; }

        [Required]
        [StringLength(150)]
        public string Substance { get; set; } = string.Empty;

        public string? ReactionType { get; set; }

        public AllergySeverity Severity { get; set; }

        public int RecordedById { get; set; }
        [ForeignKey("RecordedById")]
        public User? RecordedBy { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}
