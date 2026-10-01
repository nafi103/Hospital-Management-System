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

            var asstDocClaim = User.FindFirst("AssignedDoctorId")?.Value;
            if (!int.TryParse(asstDocClaim, out int assignedDocId) || appointment.DoctorId != assignedDocId)
            {
                return Forbid();
            }

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

            var appointment = await _context.Appointments
                .Include(a => a.Patient)
                .FirstOrDefaultAsync(a => a.Id == vital.AppointmentId);

            if (appointment == null) return NotFound();

            var asstDocClaim = User.FindFirst("AssignedDoctorId")?.Value;
            if (!int.TryParse(asstDocClaim, out int assignedDocId) || appointment.DoctorId != assignedDocId)
            {
                return Forbid();
            }

            // Clinical range boundary checks
            if (vital.SystolicBp < 40 || vital.SystolicBp > 260)
            {
                ModelState.AddModelError(nameof(vital.SystolicBp), "Systolic BP must be between 40 and 260 mmHg.");
            }
            if (vital.DiastolicBp < 20 || vital.DiastolicBp > 180)
            {
                ModelState.AddModelError(nameof(vital.DiastolicBp), "Diastolic BP must be between 20 and 180 mmHg.");
            }
            if (vital.HeartRate < 25 || vital.HeartRate > 250)
            {
                ModelState.AddModelError(nameof(vital.HeartRate), "Heart Rate must be between 25 and 250 bpm.");
            }
            if (vital.RespiratoryRate < 4 || vital.RespiratoryRate > 60)
            {
                ModelState.AddModelError(nameof(vital.RespiratoryRate), "Respiratory Rate must be between 4 and 60 breaths/min.");
            }
            if (vital.Spo2 < 50.0m || vital.Spo2 > 100.0m)
            {
                ModelState.AddModelError(nameof(vital.Spo2), "SpO2 must be between 50% and 100%.");
            }
            if (vital.Temperature < 30.0m || vital.Temperature > 45.0m)
            {
                ModelState.AddModelError(nameof(vital.Temperature), "Temperature must be between 30.0°C and 45.0°C.");
            }
            if (vital.BloodSugar.HasValue && (vital.BloodSugar.Value < 0.5m || vital.BloodSugar.Value > 50.0m))
            {
                ModelState.AddModelError(nameof(vital.BloodSugar), "Blood Sugar must be between 0.5 and 50.0 mmol/L.");
            }

            if (ModelState.IsValid)
            {
                var isChild = appointment.Patient?.IsChild == true;
                News2Calculator.Result? news2Result = null;

                if (isChild)
                {
                    // Pediatric safety gate: Adult NEWS2 scoring is clinically invalid for minors.
                    // Triage priority is withheld from automated NEWS2 scoring to prevent dangerous misclassification.
                    vital.TriagePriority = null;
                }
                else
                {
                    news2Result = News2Calculator.Calculate(
                        vital.RespiratoryRate, vital.Spo2, vital.OnSupplementalOxygen,
                        vital.Temperature, vital.SystolicBp, vital.HeartRate, vital.Consciousness);
                    vital.TriagePriority = news2Result.Value.Priority;
                }

                var userIdClaim = User.FindFirst("UserId")?.Value;
                if (int.TryParse(userIdClaim, out int userId))
                {
                    vital.RecordedById = userId;
                }
                vital.CreatedAt = DateTime.UtcNow;

                _context.PatientVitals.Add(vital);
                await _context.SaveChangesAsync();

                if (isChild)
                {
                    TempData["SuccessMessage"] = "Vitals recorded for pediatric patient. Adult NEWS2 triage score withheld (requires clinician evaluation).";
                }
                else
                {
                    TempData["SuccessMessage"] = $"Vitals recorded - triage priority: {news2Result!.Value.Priority} (NEWS2 score {news2Result.Value.Score}).";
                }

                return RedirectToAction("Index", "Appointments");
            }

            ViewBag.Appointment = appointment;
            return View(vital);
        }
    }
}
