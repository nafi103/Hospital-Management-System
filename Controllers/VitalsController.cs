using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HospitalManagementSystem.Models;
using HospitalManagementSystem.Services;

namespace HospitalManagementSystem.Controllers
{
    // Vitals are recorded against a specific appointment (not just a patient) so the
    // resulting triage badge on the queue row and the doctor's card reflects today's
    // visit, not a stale reading from a past one.
    [Authorize(Roles = "Assistant")]
    public class VitalsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public VitalsController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: Vitals/Create?appointmentId=5
        public async Task<IActionResult> Create(int appointmentId)
        {
            var appointment = await _context.Appointments
                .Include(a => a.Patient)
                .FirstOrDefaultAsync(a => a.Id == appointmentId);

            if (appointment == null) return NotFound();

            ViewBag.Appointment = appointment;
            return View(new PatientVital { AppointmentId = appointmentId, PatientId = appointment.PatientId });
        }

        // POST: Vitals/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(
            [Bind("AppointmentId,PatientId,SystolicBp,DiastolicBp,HeartRate,Spo2,Temperature,RespiratoryRate,OnSupplementalOxygen,Consciousness,BloodSugar")] PatientVital vital)
        {
            ModelState.Remove("Patient");
            ModelState.Remove("Appointment");
            ModelState.Remove("RecordedBy");

            if (ModelState.IsValid)
            {
                var result = News2Calculator.Calculate(
                    vital.RespiratoryRate, vital.Spo2, vital.OnSupplementalOxygen,
                    vital.Temperature, vital.SystolicBp, vital.HeartRate, vital.Consciousness);
                vital.TriagePriority = result.Priority;

                var userIdClaim = User.FindFirst("UserId")?.Value;
                if (int.TryParse(userIdClaim, out int userId))
                {
                    vital.RecordedById = userId;
                }
                vital.CreatedAt = DateTime.UtcNow;

                _context.PatientVitals.Add(vital);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = $"Vitals recorded - triage priority: {result.Priority} (NEWS2 score {result.Score}).";
                return RedirectToAction("Index", "Appointments");
            }

            var appointment = await _context.Appointments.Include(a => a.Patient).FirstOrDefaultAsync(a => a.Id == vital.AppointmentId);
            ViewBag.Appointment = appointment;
            return View(vital);
        }
    }
}
