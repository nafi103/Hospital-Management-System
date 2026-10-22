using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HospitalManagementSystem.Models;
using HospitalManagementSystem.Services;

namespace HospitalManagementSystem.Controllers
{
    // The front-desk landing page: today's queue at a glance, bed occupancy, outstanding
    // bills, and quick actions into the registration/booking/admission/billing flows that
    // live in their own controllers (Patients, Appointments, Admissions, Bills).
    [Authorize(Roles = "Receptionist")]
    public class ReceptionController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ReceptionController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var todayLocal = HospitalClock.Today;
            var startUtc = HospitalClock.GetStartOfDayUtc(todayLocal);
            var endUtc = HospitalClock.GetEndOfDayUtc(todayLocal);

            var appointmentStats = await _context.Appointments
                .Where(a => a.AppointmentDatetime >= startUtc && a.AppointmentDatetime < endUtc)
                .GroupBy(a => a.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();

            ViewBag.TotalToday = appointmentStats.Sum(s => s.Count);
            ViewBag.WaitingToday = appointmentStats.FirstOrDefault(s => s.Status == AppointmentStatus.Scheduled)?.Count ?? 0;
            ViewBag.InConsultationToday = appointmentStats.FirstOrDefault(s => s.Status == AppointmentStatus.InConsultation)?.Count ?? 0;
            ViewBag.CompletedToday = appointmentStats.FirstOrDefault(s => s.Status == AppointmentStatus.Completed)?.Count ?? 0;

            var totalBeds = await _context.Beds.CountAsync();
            var occupiedBeds = await _context.BedTransfers
                .Where(bt => bt.EndDate == null)
                .Select(bt => bt.BedId)
                .Distinct()
                .CountAsync();
            ViewBag.TotalBeds = totalBeds;
            ViewBag.OccupiedBeds = occupiedBeds;
            ViewBag.FreeBeds = totalBeds - occupiedBeds;

            ViewBag.ActiveAdmissions = await _context.Admissions.CountAsync(a => a.DischargeDate == null);

            var unpaidStats = await _context.Bills
                .Where(b => b.Status != BillStatus.Paid)
                .GroupBy(b => 1)
                .Select(g => new
                {
                    Count = g.Count(),
                    Outstanding = g.Sum(b => b.NetTotal - b.PaidAmount)
                })
                .FirstOrDefaultAsync();

            ViewBag.UnpaidBillCount = unpaidStats?.Count ?? 0;
            ViewBag.OutstandingAmount = unpaidStats?.Outstanding ?? 0m;

            ViewBag.RecentPatients = await _context.Patients
                .OrderByDescending(p => p.CreatedAt)
                .Take(5)
                .ToListAsync();

            // Load visual bed grid for front-desk occupancy map
            var activeTransfers = await _context.BedTransfers
                .Where(bt => bt.EndDate == null)
                .Include(bt => bt.Admission)
                    .ThenInclude(a => a.Patient)
                .ToListAsync();

            var allBeds = await _context.Beds
                .OrderBy(b => b.Category)
                .ThenBy(b => b.BedNumber)
                .Take(12)
                .ToListAsync();

            var now = DateTime.UtcNow;
            var bedGrid = allBeds.Select(b => {
                var transfer = activeTransfers.FirstOrDefault(t => t.BedId == b.Id);
                var admitted = transfer?.Admission?.AdmissionDate;
                var losDays = admitted.HasValue ? Math.Max(1, (int)(now - admitted.Value).TotalDays) : 0;

                return new ReceptionBedCardDto
                {
                    BedId = b.Id,
                    BedNumber = b.BedNumber,
                    Ward = b.Category.ToString(),
                    Category = b.Category,
                    IsOccupied = transfer != null,
                    PatientName = transfer?.Admission?.Patient?.FullName,
                    Uhid = transfer?.Admission?.Patient?.Uhid,
                    AdmissionId = transfer?.AdmissionId,
                    AdmittedAt = admitted,
                    LengthOfStayDays = losDays
                };
            }).ToList();

            ViewBag.BedGrid = bedGrid;

            return View();
        }
    }

    public class ReceptionBedCardDto
    {
        public int BedId { get; set; }
        public string BedNumber { get; set; } = string.Empty;
        public string Ward { get; set; } = string.Empty;
        public BedCategory Category { get; set; }
        public bool IsOccupied { get; set; }
        public string? PatientName { get; set; }
        public string? Uhid { get; set; }
        public int? AdmissionId { get; set; }
        public DateTime? AdmittedAt { get; set; }
        public int LengthOfStayDays { get; set; }
    }
}
