using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HospitalManagementSystem.Models;

namespace HospitalManagementSystem.Controllers
{
    // The patient-facing self-service portal. Every action resolves the caller's own
    // Patient row from their login (never from a route/query id) and filters strictly on
    // that patient's Id - a patient portal is the one place in this app where identity,
    // not role, has to gate the data, so no action here may ever trust a caller-supplied
    // patient id.
    [Authorize(Roles = "Patient")]
    public class PortalController : Controller
    {
        private readonly ApplicationDbContext _context;

        public PortalController(ApplicationDbContext context)
        {
            _context = context;
        }

        private async Task<Patient?> GetCurrentPatientAsync()
        {
            var userIdClaim = User.FindFirst("UserId")?.Value;
            if (!int.TryParse(userIdClaim, out int userId))
            {
                return null;
            }
            return await _context.Patients.FirstOrDefaultAsync(p => p.UserId == userId);
        }

        public async Task<IActionResult> Index()
        {
            var patient = await GetCurrentPatientAsync();
            if (patient == null) return Forbid();

            var now = DateTime.UtcNow;
            var appointments = await _context.Appointments
                .Include(a => a.Doctor)
                .Where(a => a.PatientId == patient.Id)
                .OrderBy(a => a.AppointmentDatetime)
                .ToListAsync();

            ViewBag.Patient = patient;
            ViewBag.Upcoming = appointments
                .Where(a => a.AppointmentDatetime >= now && a.Status != AppointmentStatus.Cancelled)
                .OrderBy(a => a.AppointmentDatetime)
                .ToList();
            ViewBag.Past = appointments
                .Where(a => a.AppointmentDatetime < now || a.Status == AppointmentStatus.Cancelled)
                .OrderByDescending(a => a.AppointmentDatetime)
                .ToList();

            return View();
        }

        public async Task<IActionResult> Records()
        {
            var patient = await GetCurrentPatientAsync();
            if (patient == null) return Forbid();

            var records = await _context.MedicalRecords
                .Include(r => r.Doctor)
                .Where(r => r.PatientId == patient.Id)
                .OrderByDescending(r => r.RecordedAt)
                .ToListAsync();

            ViewBag.Patient = patient;
            return View(records);
        }

        public async Task<IActionResult> Prescriptions()
        {
            var patient = await GetCurrentPatientAsync();
            if (patient == null) return Forbid();

            var prescriptions = await _context.Prescriptions
                .Include(p => p.Doctor)
                .Include(p => p.PrescriptionItems)
                    .ThenInclude(i => i.Medicine)
                .Where(p => p.PatientId == patient.Id)
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync();

            ViewBag.Patient = patient;
            return View(prescriptions);
        }

        public async Task<IActionResult> Bills()
        {
            var patient = await GetCurrentPatientAsync();
            if (patient == null) return Forbid();

            var bills = await _context.Bills
                .Include(b => b.BillItems)
                .Where(b => b.PatientId == patient.Id)
                .OrderByDescending(b => b.CreatedAt)
                .ToListAsync();

            ViewBag.Patient = patient;
            return View(bills);
        }
    }
}
