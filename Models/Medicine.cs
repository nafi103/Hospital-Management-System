using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace HospitalManagementSystem.Models
{
    [Index(nameof(CreatedAt))]
    public class Medicine
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        public string Name { get; set; } // Brand Name

        [Required]
        [StringLength(100)]
        public string GenericName { get; set; } = string.Empty; // e.g. Azithromycin

        public decimal UnitPrice { get; set; }
        public int StockQuantity { get; set; }


        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
