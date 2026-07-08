using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HospitalManagementSystem.Models
{
    public class BedTransfer
    {
        [Key]
        public int Id { get; set; }

        public int AdmissionId { get; set; }
        public Admission Admission { get; set; }

        public int BedId { get; set; }
        public Bed Bed { get; set; }

        public DateTime StartDate { get; set; }
        public DateTime? EndDate { get; set; }
    }
}
