using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace HospitalManagementSystem.Models
{
    [Index(nameof(MedicineId), nameof(ExpiryDate))]
    [Index(nameof(ExpiryDate))]
    public class MedicineBatch
    {
        [Key]
        public int Id { get; set; }

        public int MedicineId { get; set; }
        [ForeignKey("MedicineId")]
        public Medicine? Medicine { get; set; }

        [Required]
        [StringLength(50)]
        public string BatchNumber { get; set; } = string.Empty;

        public DateTime ExpiryDate { get; set; }

        public int QuantityRemaining { get; set; }
        public int InitialQuantity { get; set; }

        [NotMapped]
        public bool IsExpired => ExpiryDate.Date <= DateTime.UtcNow.Date;

        [NotMapped]
        public bool IsNearExpiry => !IsExpired && ExpiryDate.Date <= DateTime.UtcNow.Date.AddDays(30);

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
