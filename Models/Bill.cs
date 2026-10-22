using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace HospitalManagementSystem.Models
{
    [Index(nameof(CreatedAt))]
    public class Bill
    {
        [Key]
        public int Id { get; set; }

        public int PatientId { get; set; }
        public Patient Patient { get; set; }

        public int? AdmissionId { get; set; }
        public Admission Admission { get; set; }

        public decimal SubtotalAmount { get; set; }
        public decimal DiscountAmount { get; set; }

        public int? DiscountApprovedById { get; set; }
        [ForeignKey("DiscountApprovedById")]
        public User DiscountApprovedBy { get; set; }

        public decimal NetTotal { get; set; }
        public decimal PaidAmount { get; set; }
        public BillStatus Status { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        [Timestamp]
        public uint Version { get; set; }

        public List<BillItem> BillItems { get; set; } = new List<BillItem>();
        public List<PaymentTransaction> PaymentTransactions { get; set; } = new List<PaymentTransaction>();

        public void RecalculateTotals()
        {
            if (BillItems == null || PaymentTransactions == null)
            {
                throw new InvalidOperationException("BillItems and PaymentTransactions navigation collections must be loaded before recalculating totals.");
            }

            PaidAmount = PaymentTransactions.Sum(t => t.Amount);

            SubtotalAmount = BillItems.Sum(i => i.Amount);
            NetTotal = Math.Max(0, SubtotalAmount - DiscountAmount);
            
            // Only transition to Paid when the invoice has billable items and the balance is settled
            if (SubtotalAmount > 0 && (NetTotal == 0 || PaidAmount >= NetTotal))
                Status = BillStatus.Paid;
            else if (PaidAmount > 0)
                Status = BillStatus.PartiallyPaid;
            else
                Status = BillStatus.Unpaid;
        }
    }
}
