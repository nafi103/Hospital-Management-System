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
        public string FullName { get; set; }
        public string ContactInfo { get; set; }
        public DateTime DateOfBirth { get; set; }
        public string Gender { get; set; }
        public string BloodGroup { get; set; }
        public string EmergencyContact { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public List<Admission> Admissions { get; set; } = new List<Admission>();
    }
}