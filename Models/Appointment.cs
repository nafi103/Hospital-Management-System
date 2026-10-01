using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace HospitalManagementSystem.Models
{
    [Index(nameof(CreatedAt))]
    [Index(nameof(AppointmentDatetime))]
    [Index(nameof(DoctorId), nameof(AppointmentDatetime))]
    public class Appointment
    {
        [Key]
        public int Id { get; set; }

        public int PatientId { get; set; }
        public Patient Patient { get; set; }

        public int DoctorId { get; set; }
        [ForeignKey("DoctorId")]
        public User Doctor { get; set; }

        public DateTime AppointmentDatetime { get; set; }
        public DateTime EndTime { get; set; }
        public string ReasonForVisit { get; set; }
        public AppointmentStatus Status { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        [Timestamp]
        public uint Version { get; set; }
    }
}