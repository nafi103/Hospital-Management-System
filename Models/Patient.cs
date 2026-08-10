using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace HospitalManagementSystem.Models
{
    [Index(nameof(Uhid), IsUnique = true)]
    [Index(nameof(CreatedAt))]
    public class Patient
    {
        [Key]
        public int Id { get; set; }

        public string Uhid { get; set; }
        
        public bool IsChild { get; set; }
        
        public string? FullName { get; set; }
        
        [RegularExpression(@"^1[3-9]\d{8}$", ErrorMessage = "Phone number must be exactly 10 digits and start with a valid operator prefix (13-19).")]
        public long? ContactInfo { get; set; }
        
        public DateTime DateOfBirth { get; set; }
        public string Gender { get; set; }
        public string BloodGroup { get; set; }
        public string? EmergencyContactName { get; set; }
        
        [RegularExpression(@"^1[3-9]\d{8}$", ErrorMessage = "Phone number must be exactly 10 digits and start with a valid operator prefix (13-19).")]
        public long? EmergencyContactPhone { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public string MedicalHistoryJson { get; set; } = "[]";
        
        public int? RegisteredById { get; set; }
        public User? RegisteredBy { get; set; }

        public List<Admission> Admissions { get; set; } = new List<Admission>();
    }
}