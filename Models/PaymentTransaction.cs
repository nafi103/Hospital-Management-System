using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace HospitalManagementSystem.Models
{
    [Index(nameof(BillId))]
    [Index(nameof(TransactionDate))]
    [Index(nameof(TransactionId))]
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

        [StringLength(100)]
        public string? TransactionId { get; set; }

        public int? ProcessedById { get; set; }
        [ForeignKey("ProcessedById")]
        public User? ProcessedBy { get; set; }

        public DateTime TransactionDate { get; set; } = DateTime.UtcNow;

        [StringLength(255)]
        public string? Notes { get; set; }

        [StringLength(64)]
        public string? PreviousHash { get; set; }

        [StringLength(64)]
        public string? RowHash { get; set; }

        public static string ComputeSha256(string? previousHash, int billId, decimal amount, string paymentMethod, string? transactionId, int? processedById, DateTime transactionDate)
        {
            var raw = FormattableString.Invariant($"{previousHash ?? "GENESIS"}|{billId}|{amount:0.00}|{paymentMethod}|{transactionId ?? ""}|{processedById ?? 0}|{transactionDate:O}");
            using var sha = System.Security.Cryptography.SHA256.Create();
            var bytes = System.Text.Encoding.UTF8.GetBytes(raw);
            var hashBytes = sha.ComputeHash(bytes);
            return Convert.ToHexStringLower(hashBytes);
        }

        public string ComputeCurrentHash(string? prevHash = null)
        {
            return ComputeSha256(prevHash ?? PreviousHash, BillId, Amount, PaymentMethod, TransactionId, ProcessedById, TransactionDate);
        }
    }
}
