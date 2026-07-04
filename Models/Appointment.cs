using System;

namespace HospitalManagementSystem.Models
{
    public class Appointment
    {
        public int Id { get; set; }
        public int PatientId { get; set; }
        public Patient Patient { get; set; }
        
        public int DoctorId { get; set; }
        public Doctor Doctor { get; set; }
        
        public DateTime AppointmentDate { get; set; }
        public string Status { get; set; } // Scheduled, Completed, Cancelled
        public string Reason { get; set; }
    }
}