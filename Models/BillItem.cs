using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace HospitalManagementSystem.Models
{
    public class BillItem
    {
        [Key]
        public int Id { get; set; }

        public int BillId { get; set; }
        public Bill Bill { get; set; }

        public DepartmentType Department { get; set; }
        public string Description { get; set; }
        public decimal Amount { get; set; }
    }
}
