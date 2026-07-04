using System;

namespace HospitalManagementSystem.Models
{
    public class Billing
    {
        public int Id { get; set; }
        
        public int AdmissionId { get; set; } // কোন ভর্তির বিল
        public Admission Admission { get; set; }
        
        public decimal TotalAmount { get; set; }
        public decimal PaidAmount { get; set; }
        public string PaymentStatus { get; set; } // Paid, Pending, Partial
        public DateTime InvoiceDate { get; set; }
    }
}