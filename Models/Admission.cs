using System;
using System.ComponentModel.DataAnnotations;

namespace HospitalManagementSystem.Models
{
    public class Admission
    {
        [Key]
        public int Id { get; set; } // id (PK) - INT

        // ফরেন কি (Foreign Keys) - এগুলো দিয়ে অন্য টেবিলের সাথে লিংক হবে
        public int PatientId { get; set; } // patient_id (FK)
        
        public int BedId { get; set; } // bed_id (FK)[cite: 1]
        
        public int AdmittingDoctorId { get; set; } // admitting_doctor_id (FK)[cite: 1]

        // সময় এবং তারিখ রাখার জন্য DateTime ব্যবহার করা হয়
        public DateTime AdmissionDate { get; set; } // admission_date[cite: 1]
        
        // DateTime এর পর '?' দেওয়ার মানে হলো এটি Nullable
        public DateTime? DischargeDate { get; set; } // discharge_date (Nullable)[cite: 1]
    }
}