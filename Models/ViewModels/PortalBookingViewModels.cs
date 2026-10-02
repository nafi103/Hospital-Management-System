using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace HospitalManagementSystem.Models.ViewModels
{
    public class DoctorDirectoryItemViewModel
    {
        public int Id { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public DateTime? NextAvailableSlot { get; set; }
        public string? NextAvailableSlotDisplay { get; set; }
    }

    public class AvailableSlotDto
    {
        public string TimeDisplay { get; set; } = string.Empty;
        public DateTime SlotStartUtc { get; set; }
        public DateTime SlotEndUtc { get; set; }
        public bool IsAvailable { get; set; }
        public string? ConflictReason { get; set; }
        public string Shift { get; set; } = "Morning"; // "Morning" or "Afternoon"
    }

    public class DailySlotsResponseDto
    {
        public string Date { get; set; } = string.Empty;
        public int DoctorId { get; set; }
        public string DoctorName { get; set; } = string.Empty;
        public List<AvailableSlotDto> MorningSlots { get; set; } = new();
        public List<AvailableSlotDto> AfternoonSlots { get; set; } = new();
        public int TotalAvailable { get; set; }
    }

    public class BookAppointmentViewModel
    {
        [Required(ErrorMessage = "Please select a doctor.")]
        [Display(Name = "Doctor")]
        public int DoctorId { get; set; }

        public string? DoctorName { get; set; }
        public string? DoctorCategory { get; set; }

        [Required(ErrorMessage = "Please select an appointment date.")]
        [Display(Name = "Appointment Date")]
        public string SelectedDate { get; set; } = string.Empty; // Format: yyyy-MM-dd

        [Required(ErrorMessage = "Please select an available time slot.")]
        [Display(Name = "Time Slot")]
        public string SelectedSlotTime { get; set; } = string.Empty; // ISO 8601 UTC string

        [Required(ErrorMessage = "Please provide the reason for your visit.")]
        [StringLength(500, MinimumLength = 3, ErrorMessage = "Reason must be between 3 and 500 characters.")]
        [Display(Name = "Reason for Visit")]
        public string ReasonForVisit { get; set; } = string.Empty;

        public List<SelectListItem>? AvailableDoctors { get; set; }
    }

    public class RescheduleAppointmentViewModel
    {
        public int AppointmentId { get; set; }
        public int DoctorId { get; set; }
        public string? DoctorName { get; set; }
        public string? DoctorCategory { get; set; }

        public DateTime CurrentDatetimeUtc { get; set; }
        public string? CurrentDatetimeDisplay { get; set; }

        [Required(ErrorMessage = "Please select a new appointment date.")]
        [Display(Name = "New Date")]
        public string SelectedDate { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please select a new available time slot.")]
        [Display(Name = "New Time Slot")]
        public string SelectedSlotTime { get; set; } = string.Empty;

        [StringLength(500, ErrorMessage = "Reason cannot exceed 500 characters.")]
        [Display(Name = "Reason for Visit")]
        public string? ReasonForVisit { get; set; }

        public uint Version { get; set; }
    }
}
