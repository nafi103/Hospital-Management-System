using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace HospitalManagementSystem.Models
{
    public class PrescriptionItem
    {
        [Key]
        public int Id { get; set; }

        public int PrescriptionId { get; set; }
        public Prescription? Prescription { get; set; }

        public int MedicineId { get; set; }
        public Medicine? Medicine { get; set; }

        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }

        // Structured dose, replacing the old free-text "1 + 0 + 1" convention.
        public decimal DoseMorning { get; set; }
        public decimal DoseAfternoon { get; set; }
        public decimal DoseEvening { get; set; }
        public DoseUnit DoseUnit { get; set; } = DoseUnit.Tablet;
        public int? DurationDays { get; set; }
        public MedicationRoute Route { get; set; } = MedicationRoute.Oral;

        public string? Instructions { get; set; }

        [NotMapped]
        public string DoseDisplay =>
            $"{DoseMorning.ToString("0.##", CultureInfo.InvariantCulture)}+{DoseAfternoon.ToString("0.##", CultureInfo.InvariantCulture)}+{DoseEvening.ToString("0.##", CultureInfo.InvariantCulture)} {DoseUnit}";
    }
}
