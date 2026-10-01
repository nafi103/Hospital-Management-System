using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace HospitalManagementSystem.Models
{
    [Index(nameof(BillId))]
    [Index(nameof(TransactionDate))]
    public class PaymentTransaction
    {
        [Key]
        public int Id { get; set; }

        public int BillId { get; set; }
        [ForeignKey("BillId")]
        public Bill? Bill { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        [Range(0.01, 10000000.00)]
        public decimal Amount { get; set; }

        [Required]
        [StringLength(50)]
        public string PaymentMethod { get; set; } = "Cash";

        public int? ProcessedById { get; set; }
        [ForeignKey("ProcessedById")]
        public User? ProcessedBy { get; set; }

        public DateTime TransactionDate { get; set; } = DateTime.UtcNow;

        [StringLength(255)]
        public string? Notes { get; set; }
    }
}
