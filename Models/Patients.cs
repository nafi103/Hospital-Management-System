using System.ComponentModel.DataAnnotations;

namespace HospitalManagementSystem.Models
{
    // সিপিতে যেমন struct Patient লিখো, এটা ঠিক তেমনই
    public class Patient
    {
        [Key]
        public int Id { get; set; } // id (PK) - INT

        public string Uhid { get; set; } // uhid - VARCHAR (Unique Hospital Identification Number)

        public string FullName { get; set; } // full_name - VARCHAR

        public string ContactInfo { get; set; } // contact_info - VARCHAR

        public string CurrentStatus { get; set; } // current_status - VARCHAR (e.g., 'Waiting', 'In Consultation', 'Admitted', 'Cleared for Discharge')[cite: 1]
    }
}