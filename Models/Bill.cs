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

        public List<BillItem> BillItems { get; set; } = new List<BillItem>();

        public void RecalculateTotals()
        {
            SubtotalAmount = BillItems?.Sum(i => i.Amount) ?? 0;
            NetTotal = SubtotalAmount - DiscountAmount;
            
            if (PaidAmount >= NetTotal && NetTotal > 0)
                Status = BillStatus.Paid;
            else if (PaidAmount > 0)
                Status = BillStatus.PartiallyPaid;
            else
                Status = BillStatus.Unpaid;
        }
    }
}
