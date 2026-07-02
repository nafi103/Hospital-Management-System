using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace HospitalManagementSystem.Models
{
    public class Role
    {
        [Key]
        public int Id { get; set; } // id (PK) [cite: 1, 16]

        public string RoleName { get; set; } // role_name - VARCHAR [cite: 1, 17]

        // পিডিএফ অনুযায়ী এটি JSON ফরম্যাটে থাকবে, C#-এ আমরা আপাতত এটিকে string হিসেবে রাখব [cite: 1, 18]
        public string Permissions { get; set; }

        // রিলেশনশিপ: একটি রোলের আন্ডারে অনেক ইউজার থাকতে পারে
        public List<User> Users { get; set; } = new List<User>();
    }
}