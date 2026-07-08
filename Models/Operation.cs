using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace HospitalManagementSystem.Models
{
    [Index(nameof(CreatedAt))]
    public class Operation
    {
        [Key]
        public int Id { get; set; }

        public int PatientId { get; set; }
        public Patient Patient { get; set; }

        public int SurgeonId { get; set; }
        [ForeignKey("SurgeonId")]
        public User Surgeon { get; set; }

        public string OtRoom { get; set; }
        public DateTime OperationDatetime { get; set; }
        public string PreparationChecklist { get; set; }
        public OperationStatus Status { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
