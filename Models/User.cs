using System.ComponentModel.DataAnnotations;

namespace HospitalManagementSystem.Models
{
    public class User
    {
        [Key]
        public int Id { get; set; } // id (PK) [cite: 1, 21]

        // Foreign Key (কোন রোলের আন্ডারে এই ইউজার) [cite: 1, 22]
        public int RoleId { get; set; }

        public string Username { get; set; } // username [cite: 1, 23]

        public string PasswordHash { get; set; } // password_hash [cite: 1, 24]

        public string FullName { get; set; } // full_name [cite: 1, 25]

        public string Department { get; set; } // department (e.g., 'Cardiology', 'Pharmacy') [cite: 1, 26]
    }
}