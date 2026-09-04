using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace HospitalManagementSystem.Models
{
    public class BillItem
    {
        [Key]
        public int Id { get; set; }

        public int BillId { get; set; }
        // Nullable so posting a bare List<BillItem> from Bills/Create doesn't trip
        // ASP.NET Core's implicit-required validation for non-nullable reference types -
        // the form can never populate this nav property, only BillId. Mirrors
        // PrescriptionItem.Prescription, which already uses this same pattern.
        public Bill? Bill { get; set; }

        public DepartmentType Department { get; set; }
        public string Description { get; set; }
        public decimal Amount { get; set; }
    }
}
