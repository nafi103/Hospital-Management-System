using System.ComponentModel.DataAnnotations;

namespace HospitalManagementSystem.Models
{
    // সিপিতে যেমন struct Patient লিখো, এটা ঠিক তেমনই
    public class Bed
    {
        [Key]
        public int Id { get; set; } // id (PK) - INT
        public string BedNumber { get; set; } // bed_number - VARCHAR (Unique Bed Number)
        public string Type { get; set; } // bed_type - VARCHAR (e.g., 'General', 'ICU', 'Private')[cite: 1]
        public string Status { get; set; } // status - VARCHAR (e.g., 'Available', 'Occupied', 'Under Maintenance')[cite: 1]
        public decimal DailyRate { get; set; } // daily_rate - DECIMAL (Cost per day for the bed)[cite: 1]
        public List<Admission> Admissions { get; set; } = new List<Admission>(); // Admissions - List of Admission objects (One-to-Many relationship)
    }
}