using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace HospitalManagementSystem.Models
{
    [Index(nameof(Username), IsUnique = true)]
    [Index(nameof(CreatedAt))]
    public class User
    {
        [Key]
        public int Id { get; set; }

        public int RoleId { get; set; }
        public Role Role { get; set; }

        public string Username { get; set; }

        // Form-binding only; never persisted. Hashed into PasswordHash before save.
        [NotMapped]
        public string Password { get; set; }

        public string PasswordHash { get; set; } = string.Empty;

        public string FullName { get; set; }
        public string Category { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public int? AssignedDoctorId { get; set; }
        public User? AssignedDoctor { get; set; }
    }
}