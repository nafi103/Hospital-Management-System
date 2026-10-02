using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using HospitalManagementSystem.Models;
using HospitalManagementSystem.Services;

namespace HospitalManagementSystem.Models.ViewModels
{
    public class ClinicalCockpitViewModel
    {
        public Appointment Appointment { get; set; } = null!;
        public Patient Patient { get; set; } = null!;
        public string AgeDisplay { get; set; } = string.Empty;
        public List<PatientAllergy> Allergies { get; set; } = new();
        public PatientVital? LatestVitals { get; set; }
        public News2Calculator.Result? News2Result { get; set; }
        public List<ActiveMedicationInfo> ActiveMedications { get; set; } = new();
        public AiSuggestion? LatestAiCaseSummary { get; set; }
        public List<CockpitMedicineDto> FormularyMedicines { get; set; } = new();

        // Existing encounter records if already partially drafted
        public int? ExistingMedicalRecordId { get; set; }
        public string? ChiefComplaint { get; set; }
        public string? Diagnosis { get; set; }
        public string? Treatment { get; set; }

        public int? ExistingPrescriptionId { get; set; }
        public List<PrescriptionItem> ExistingPrescriptionItems { get; set; } = new();
    }

    public class CockpitMedicineDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string GenericName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public decimal UnitPrice { get; set; }
        public int StockQuantity { get; set; }
        public string Strength { get; set; } = string.Empty;
    }

    public class ClinicalCockpitSubmitModel
    {
        [Required]
        public int AppointmentId { get; set; }

        [Required]
        public int PatientId { get; set; }

        public string? ChiefComplaint { get; set; }

        [Required(ErrorMessage = "Clinical diagnosis is required to complete the consultation.")]
        public string Diagnosis { get; set; } = string.Empty;

        public string? Treatment { get; set; }

        public List<CockpitPrescriptionItemInput> PrescriptionItems { get; set; } = new();

        public string? SafetyOverrideReason { get; set; }
    }

    public class CockpitPrescriptionItemInput
    {
        public int MedicineId { get; set; }
        public int Quantity { get; set; }
        public decimal DoseMorning { get; set; }
        public decimal DoseAfternoon { get; set; }
        public decimal DoseEvening { get; set; }
        public DoseUnit DoseUnit { get; set; } = DoseUnit.Tablet;
        public int? DurationDays { get; set; }
        public MedicationRoute Route { get; set; } = MedicationRoute.Oral;
        public string? Instructions { get; set; }
    }
}
