using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HospitalManagementSystem.Models;

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
            var today = DateTime.UtcNow.Date;
            var todaysAppointments = await _context.Appointments
                .Where(a => a.AppointmentDatetime >= today && a.AppointmentDatetime < today.AddDays(1))
                .ToListAsync();

            ViewBag.TotalToday = todaysAppointments.Count;
            ViewBag.WaitingToday = todaysAppointments.Count(a => a.Status == AppointmentStatus.Scheduled);
            ViewBag.InConsultationToday = todaysAppointments.Count(a => a.Status == AppointmentStatus.InConsultation);
            ViewBag.CompletedToday = todaysAppointments.Count(a => a.Status == AppointmentStatus.Completed);

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

            var unpaidBills = await _context.Bills
                .Where(b => b.Status != BillStatus.Paid)
                .ToListAsync();
            ViewBag.UnpaidBillCount = unpaidBills.Count;
            ViewBag.OutstandingAmount = unpaidBills.Sum(b => b.NetTotal - b.PaidAmount);

            ViewBag.RecentPatients = await _context.Patients
                .OrderByDescending(p => p.CreatedAt)
                .Take(5)
                .ToListAsync();

            return View();
        }
    }
}
