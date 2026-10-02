using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using HospitalManagementSystem.Models;
using HospitalManagementSystem.Models.ViewModels;

namespace HospitalManagementSystem.Services
{
    public class DoctorScheduleService : IAppointmentBookingService
    {
        private readonly ApplicationDbContext _context;

        public const int SlotDurationMinutes = 15;
        public const int StartHour = 9;   // 09:00 AM
        public const int EndHour = 17;    // 05:00 PM (17:00)

        public DoctorScheduleService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<DailySlotsResponseDto> GetDailySlotsAsync(
            int doctorId,
            DateTime localDate,
            int? excludeAppointmentId = null,
            CancellationToken ct = default)
        {
            var doctor = await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == doctorId, ct);

            var doctorName = doctor != null ? $"Dr. {doctor.FullName}" : "Doctor";
            var localDateOnly = localDate.Date;
            var dayStartUtc = HospitalClock.GetStartOfDayUtc(localDateOnly);
            var dayEndUtc = HospitalClock.GetEndOfDayUtc(localDateOnly);

            var query = _context.Appointments
                .AsNoTracking()
                .Where(a => a.DoctorId == doctorId
                         && a.Status != AppointmentStatus.Cancelled
                         && a.Status != AppointmentStatus.Completed
                         && a.AppointmentDatetime < dayEndUtc
                         && a.EndTime > dayStartUtc);

            if (excludeAppointmentId.HasValue)
            {
                query = query.Where(a => a.Id != excludeAppointmentId.Value);
            }

            var booked = await query
                .Select(a => new { a.AppointmentDatetime, a.EndTime })
                .ToListAsync(ct);

            var morningSlots = new List<AvailableSlotDto>();
            var afternoonSlots = new List<AvailableSlotDto>();
            var nowUtc = DateTime.UtcNow;
            int totalAvailable = 0;

            // Generate slots from 09:00 to 17:00
            for (int hour = StartHour; hour < EndHour; hour++)
            {
                for (int minute = 0; minute < 60; minute += SlotDurationMinutes)
                {
                    var slotStartLocal = localDateOnly.AddHours(hour).AddMinutes(minute);
                    var slotEndLocal = slotStartLocal.AddMinutes(SlotDurationMinutes);

                    var slotStartUtc = TimeZoneInfo.ConvertTimeToUtc(
                        DateTime.SpecifyKind(slotStartLocal, DateTimeKind.Unspecified),
                        HospitalClock.TimeZone);
                    var slotEndUtc = slotStartUtc.AddMinutes(SlotDurationMinutes);

                    bool isAvailable = true;
                    string? reason = null;

                    // Lead-time check: Same-day slot starting within 15 mins of now is unavailable
                    if (slotStartUtc <= nowUtc.AddMinutes(15))
                    {
                        isAvailable = false;
                        reason = "Past slot";
                    }
                    else
                    {
                        // Overlapping conflict check: (AppointmentDatetime < slotEndUtc && EndTime > slotStartUtc)
                        var conflict = booked.Any(b => b.AppointmentDatetime < slotEndUtc && b.EndTime > slotStartUtc);
                        if (conflict)
                        {
                            isAvailable = false;
                            reason = "Booked";
                        }
                    }

                    if (isAvailable)
                    {
                        totalAvailable++;
                    }

                    var shift = hour < 13 ? "Morning" : "Afternoon";
                    var slotDto = new AvailableSlotDto
                    {
                        TimeDisplay = slotStartLocal.ToString("hh:mm tt", CultureInfo.InvariantCulture),
                        SlotStartUtc = slotStartUtc,
                        SlotEndUtc = slotEndUtc,
                        IsAvailable = isAvailable,
                        ConflictReason = reason,
                        Shift = shift
                    };

                    if (shift == "Morning")
                    {
                        morningSlots.Add(slotDto);
                    }
                    else
                    {
                        afternoonSlots.Add(slotDto);
                    }
                }
            }

            return new DailySlotsResponseDto
            {
                Date = localDateOnly.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                DoctorId = doctorId,
                DoctorName = doctorName,
                MorningSlots = morningSlots,
                AfternoonSlots = afternoonSlots,
                TotalAvailable = totalAvailable
            };
        }

        public async Task<bool> CheckDoctorCollisionAsync(
            int doctorId,
            DateTime startUtc,
            DateTime endUtc,
            int? excludeAppointmentId = null,
            CancellationToken ct = default)
        {
            var query = _context.Appointments
                .AsNoTracking()
                .Where(a => a.DoctorId == doctorId
                         && a.Status != AppointmentStatus.Cancelled
                         && a.Status != AppointmentStatus.Completed
                         && a.AppointmentDatetime < endUtc
                         && a.EndTime > startUtc);

            if (excludeAppointmentId.HasValue)
            {
                query = query.Where(a => a.Id != excludeAppointmentId.Value);
            }

            return await query.AnyAsync(ct);
        }

        public async Task<bool> CheckPatientCollisionAsync(
            int patientId,
            DateTime startUtc,
            DateTime endUtc,
            int? excludeAppointmentId = null,
            CancellationToken ct = default)
        {
            var query = _context.Appointments
                .AsNoTracking()
                .Where(a => a.PatientId == patientId
                         && a.Status != AppointmentStatus.Cancelled
                         && a.Status != AppointmentStatus.Completed
                         && a.AppointmentDatetime < endUtc
                         && a.EndTime > startUtc);

            if (excludeAppointmentId.HasValue)
            {
                query = query.Where(a => a.Id != excludeAppointmentId.Value);
            }

            return await query.AnyAsync(ct);
        }

        public async Task<DateTime?> GetNextAvailableSlotAsync(int doctorId, CancellationToken ct = default)
        {
            var today = HospitalClock.Today;

            // Search the next 14 calendar days
            for (int i = 0; i < 14; i++)
            {
                var targetDate = today.AddDays(i);
                var dailySlots = await GetDailySlotsAsync(doctorId, targetDate, null, ct);

                var firstAvailable = dailySlots.MorningSlots.Concat(dailySlots.AfternoonSlots)
                    .FirstOrDefault(s => s.IsAvailable);

                if (firstAvailable != null)
                {
                    return firstAvailable.SlotStartUtc;
                }
            }

            return null;
        }

        public async Task<List<DoctorDirectoryItemViewModel>> GetDoctorDirectoryAsync(
            string? search = null,
            string? category = null,
            CancellationToken ct = default)
        {
            var doctorsQuery = _context.Users
                .AsNoTracking()
                .Include(u => u.Role)
                .Where(u => u.Role.RoleName == "Doctor");

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToLower();
                doctorsQuery = doctorsQuery.Where(u =>
                    u.FullName.ToLower().Contains(term) ||
                    (u.Category != null && u.Category.ToLower().Contains(term)));
            }

            if (!string.IsNullOrWhiteSpace(category) && category != "All")
            {
                doctorsQuery = doctorsQuery.Where(u => u.Category == category);
            }

            var doctors = await doctorsQuery
                .OrderBy(u => u.FullName)
                .ToListAsync(ct);

            var result = new List<DoctorDirectoryItemViewModel>();
            foreach (var doc in doctors)
            {
                var nextSlot = await GetNextAvailableSlotAsync(doc.Id, ct);
                string? nextSlotDisplay = null;
                if (nextSlot.HasValue)
                {
                    var localTime = nextSlot.Value.ToHospitalTime();
                    var isToday = localTime.Date == HospitalClock.Today;
                    var isTomorrow = localTime.Date == HospitalClock.Today.AddDays(1);

                    var dayPrefix = isToday ? "Today" : isTomorrow ? "Tomorrow" : localTime.ToString("ddd, MMM dd");
                    nextSlotDisplay = $"{dayPrefix} at {localTime:hh:mm tt}";
                }

                result.Add(new DoctorDirectoryItemViewModel
                {
                    Id = doc.Id,
                    FullName = doc.FullName,
                    Category = string.IsNullOrWhiteSpace(doc.Category) ? "General Physician" : doc.Category,
                    NextAvailableSlot = nextSlot,
                    NextAvailableSlotDisplay = nextSlotDisplay
                });
            }

            return result;
        }
    }
}
