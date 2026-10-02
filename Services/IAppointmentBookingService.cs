using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HospitalManagementSystem.Models.ViewModels;

namespace HospitalManagementSystem.Services
{
    public interface IAppointmentBookingService
    {
        /// <summary>
        /// Generates all 15-minute consultation slots (09:00 to 17:00) for a doctor on a given local date,
        /// marking slots as available or unavailable based on existing bookings and current time.
        /// </summary>
        Task<DailySlotsResponseDto> GetDailySlotsAsync(
            int doctorId,
            DateTime localDate,
            int? excludeAppointmentId = null,
            CancellationToken ct = default);

        /// <summary>
        /// Checks whether the doctor has an overlapping active appointment in the given UTC interval.
        /// </summary>
        Task<bool> CheckDoctorCollisionAsync(
            int doctorId,
            DateTime startUtc,
            DateTime endUtc,
            int? excludeAppointmentId = null,
            CancellationToken ct = default);

        /// <summary>
        /// Checks whether the patient already has an overlapping active appointment in the given UTC interval.
        /// </summary>
        Task<bool> CheckPatientCollisionAsync(
            int patientId,
            DateTime startUtc,
            DateTime endUtc,
            int? excludeAppointmentId = null,
            CancellationToken ct = default);

        /// <summary>
        /// Returns the earliest available 15-minute slot for a doctor across the next 14 calendar days.
        /// </summary>
        Task<DateTime?> GetNextAvailableSlotAsync(int doctorId, CancellationToken ct = default);

        /// <summary>
        /// Retrieves the list of active doctors with their specialties and next available slot.
        /// </summary>
        Task<List<DoctorDirectoryItemViewModel>> GetDoctorDirectoryAsync(
            string? search = null,
            string? category = null,
            CancellationToken ct = default);
    }
}
