using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using HospitalManagementSystem.Models;

namespace HospitalManagementSystem.Models.ViewModels
{
    public class OperationCreateViewModel
    {
        [Required(ErrorMessage = "Please select a patient.")]
        [Display(Name = "Patient")]
        public int PatientId { get; set; }

        [Required(ErrorMessage = "Please select a surgeon.")]
        [Display(Name = "Surgeon")]
        public int SurgeonId { get; set; }

        [Required(ErrorMessage = "Please select an Operating Theatre.")]
        [Display(Name = "Operating Theatre Room")]
        public string OtRoom { get; set; } = "OT-1";

        [Required(ErrorMessage = "Please specify the operation date and time.")]
        [Display(Name = "Operation Date & Time")]
        public DateTime OperationDatetime { get; set; } = DateTime.UtcNow.AddDays(1);

        [Required(ErrorMessage = "Please select an ICU or Cabin recovery bed.")]
        [Display(Name = "Post-Op Recovery Bed (ICU / Cabin)")]
        public int RecoveryBedId { get; set; }

        [Display(Name = "Preparation Checklist & Clinical Protocol")]
        public string PreparationChecklist { get; set; } = "NPO (Fasting 8h) Confirmed; Pre-op Anesthesia Clearance Obtained; Surgical Consent Signed; Cross-matched Blood Reserved.";
    }

    public class OperationListItemViewModel
    {
        public int Id { get; set; }
        public int PatientId { get; set; }
        public string PatientName { get; set; } = string.Empty;
        public string Uhid { get; set; } = string.Empty;
        public int SurgeonId { get; set; }
        public string SurgeonName { get; set; } = string.Empty;
        public string OtRoom { get; set; } = string.Empty;
        public DateTime OperationDatetime { get; set; }
        public OperationStatus Status { get; set; }
        public string PreparationChecklist { get; set; } = string.Empty;
    }
}
