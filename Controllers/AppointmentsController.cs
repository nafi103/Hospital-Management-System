using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using HospitalManagementSystem.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using HospitalManagementSystem.Hubs;
using HospitalManagementSystem.Services;

namespace HospitalManagementSystem.Controllers
{
    [Authorize(Roles = "Assistant,Receptionist")]
    public class AppointmentsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IHubContext<NotificationHub> _hubContext;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<AppointmentsController> _logger;

        public AppointmentsController(
            ApplicationDbContext context,
            IHubContext<NotificationHub> hubContext,
            IServiceScopeFactory scopeFactory,
            ILogger<AppointmentsController> logger)
        {
            _context = context;
            _hubContext = hubContext;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        // GET: Appointments
        public async Task<IActionResult> Index()
        {
            // Show today's active queue in hospital operational timezone
            var todayLocal = HospitalClock.Today;
            var startUtc = HospitalClock.GetStartOfDayUtc(todayLocal);
            var endUtc = HospitalClock.GetEndOfDayUtc(todayLocal);

            var query = _context.Appointments
                .Include(a => a.Patient)
                .Include(a => a.Doctor)
                .Where(a => a.AppointmentDatetime >= startUtc && a.AppointmentDatetime < endUtc)
                .AsQueryable();

            if (User.IsInRole("Assistant"))
            {
                var doctorIdClaim = User.Claims.FirstOrDefault(c => c.Type == "AssignedDoctorId")?.Value;
                if (int.TryParse(doctorIdClaim, out int docId))
                {
                    query = query.Where(a => a.DoctorId == docId);
                }
            }

            var appointments = await query
                .OrderBy(a => a.Status == AppointmentStatus.Completed ? 1 : 0) // Completed at the bottom
                .ThenBy(a => a.AppointmentDatetime) // Oldest waiting first
                .ToListAsync();

            // Grouped in memory rather than ToDictionaryAsync - a re-recorded vitals
            // entry would otherwise throw on a duplicate AppointmentId key. Latest wins.
            var appointmentIds = appointments.Select(a => a.Id).ToList();
            ViewBag.TriageByAppointment = (await _context.PatientVitals
                    .Where(v => v.AppointmentId.HasValue && appointmentIds.Contains(v.AppointmentId.Value))
                    .OrderByDescending(v => v.CreatedAt)
                    .ToListAsync())
                .GroupBy(v => v.AppointmentId!.Value)
                .ToDictionary(g => g.Key, g => g.First().TriagePriority);

            return View(appointments);
        }

        // GET: Appointments/Create
        public IActionResult Create()
        {
            var doctorsQuery = _context.Users
                .Include(u => u.Role)
                .Where(u => u.Role.RoleName == "Doctor")
                .AsQueryable();

            if (User.IsInRole("Assistant"))
            {
                var doctorIdClaim = User.Claims.FirstOrDefault(c => c.Type == "AssignedDoctorId")?.Value;
                if (int.TryParse(doctorIdClaim, out int docId))
                {
                    doctorsQuery = doctorsQuery.Where(u => u.Id == docId);
                }
            }

            var doctors = doctorsQuery.Select(u => new { u.Id, u.FullName }).ToList();
            ViewData["DoctorId"] = new SelectList(doctors, "Id", "FullName");

            var nowLocal = HospitalClock.Now;
            var defaultDateTime = new DateTime(nowLocal.Year, nowLocal.Month, nowLocal.Day, nowLocal.Hour, nowLocal.Minute, 0);

            return View(new Appointment
            {
                AppointmentDatetime = defaultDateTime,
                Status = AppointmentStatus.Scheduled
            });
        }

        // POST: Appointments/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Id,PatientId,DoctorId,AppointmentDatetime,ReasonForVisit,Status")] Appointment appointment)
        {
            ModelState.Remove("Patient");
            ModelState.Remove("Doctor");

            if (User.IsInRole("Assistant"))
            {
                var doctorIdClaim = User.Claims.FirstOrDefault(c => c.Type == "AssignedDoctorId")?.Value;
                if (int.TryParse(doctorIdClaim, out int docId))
                {
                    appointment.DoctorId = docId;
                }
                appointment.Status = AppointmentStatus.Scheduled;
            }

            // Convert local appointment datetime to UTC using HospitalClock operational timezone
            var localDateTime = DateTime.SpecifyKind(appointment.AppointmentDatetime, DateTimeKind.Unspecified);
            var appointmentUtc = TimeZoneInfo.ConvertTimeToUtc(localDateTime, HospitalClock.TimeZone);
            appointment.AppointmentDatetime = appointmentUtc;
            appointment.EndTime = appointmentUtc.AddMinutes(15);

            // Validation: Cannot schedule in past (allow 15-minute grace for slight clock differences)
            if (appointment.AppointmentDatetime < DateTime.UtcNow.AddMinutes(-15))
            {
                ModelState.AddModelError("AppointmentDatetime", "Cannot schedule an appointment in the past.");
            }

            // Conflict Detection: check if doctor has an active overlapping appointment
            if (appointment.DoctorId > 0)
            {
                var newStart = appointment.AppointmentDatetime;
                var newEnd = appointment.EndTime;
                var conflict = await _context.Appointments
                    .Include(a => a.Patient)
                    .Where(a => a.DoctorId == appointment.DoctorId
                             && a.Status != AppointmentStatus.Cancelled
                             && a.Status != AppointmentStatus.Completed
                             && a.AppointmentDatetime < newEnd
                             && a.EndTime > newStart)
                    .FirstOrDefaultAsync();

                if (conflict != null)
                {
                    var conflictLocalStart = conflict.AppointmentDatetime.ToHospitalTime();
                    var patientName = conflict.Patient?.FullName ?? "Unknown Patient";
                    ModelState.AddModelError("AppointmentDatetime",
                        $"Schedule Conflict: Doctor already has an appointment booked at {conflictLocalStart:hh:mm tt} for {patientName} (Status: {conflict.Status}). Please select another time slot.");
                }
            }

            if (ModelState.IsValid)
            {
                appointment.CreatedAt = DateTime.UtcNow;
                appointment.UpdatedAt = DateTime.UtcNow;

                _context.Add(appointment);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Appointment scheduled successfully.";
                return RedirectToAction(nameof(Index));
            }
            
            var doctorsQuery = _context.Users
                .Include(u => u.Role)
                .Where(u => u.Role.RoleName == "Doctor")
                .AsQueryable();
                
            if (User.IsInRole("Assistant"))
            {
                var doctorIdClaim = User.Claims.FirstOrDefault(c => c.Type == "AssignedDoctorId")?.Value;
                if (int.TryParse(doctorIdClaim, out int docId))
                {
                    doctorsQuery = doctorsQuery.Where(u => u.Id == docId);
                }
            }

            var doctors = doctorsQuery.Select(u => new { u.Id, u.FullName }).ToList();
            ViewData["DoctorId"] = new SelectList(doctors, "Id", "FullName", appointment.DoctorId);

            // Re-convert to local time for re-rendering the datetime picker
            appointment.AppointmentDatetime = appointment.AppointmentDatetime.ToHospitalTime();
            return View(appointment);
        }

        // GET: Appointments/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var appointment = await _context.Appointments
                .Include(a => a.Patient)
                .FirstOrDefaultAsync(a => a.Id == id);
                
            if (appointment == null)
            {
                return NotFound();
            }

            var doctors = _context.Users
                .Include(u => u.Role)
                .Where(u => u.Role.RoleName == "Doctor")
                .Select(u => new { u.Id, u.FullName })
                .ToList();
            ViewData["DoctorId"] = new SelectList(doctors, "Id", "FullName", appointment.DoctorId);
            return View(appointment);
        }

        // POST: Appointments/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,PatientId,DoctorId,ReasonForVisit,Status,Version")] Appointment appointment)
        {
            if (id != appointment.Id)
            {
                return NotFound();
            }

            ModelState.Remove("Patient");
            ModelState.Remove("Doctor");

            var tracked = await _context.Appointments
                .Include(a => a.Patient)
                .FirstOrDefaultAsync(a => a.Id == id);

            if (tracked == null)
            {
                return NotFound();
            }

            // State machine validation: terminal appointments cannot revert to Scheduled or InConsultation
            if ((tracked.Status == AppointmentStatus.Completed || tracked.Status == AppointmentStatus.Cancelled)
                && appointment.Status != tracked.Status)
            {
                ModelState.AddModelError("Status", $"Cannot change the status of an appointment that is already {tracked.Status}. Terminal appointments cannot be reopened.");
            }

            if (appointment.Version > 0 && _context.Database.IsRelational())
            {
                try
                {
                    _context.Entry(tracked).Property(a => a.Version).OriginalValue = appointment.Version;
                }
                catch
                {
                    // Fallback
                }
            }

            if (ModelState.IsValid)
            {
                try
                {
                    tracked.DoctorId = appointment.DoctorId;
                    tracked.Status = appointment.Status;
                    tracked.ReasonForVisit = appointment.ReasonForVisit;
                    tracked.UpdatedAt = DateTime.UtcNow;

                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = "Appointment updated successfully.";
                    return RedirectToAction(nameof(Index));
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!AppointmentExists(appointment.Id))
                    {
                        return NotFound();
                    }
                    ModelState.AddModelError("", "This appointment was modified by another clinician or staff member while you were editing it. Please refresh the page to view the latest status.");
                }
            }

            var doctors = _context.Users
                .Include(u => u.Role)
                .Where(u => u.Role.RoleName == "Doctor")
                .Select(u => new { u.Id, u.FullName })
                .ToList();
            ViewData["DoctorId"] = new SelectList(doctors, "Id", "FullName", appointment.DoctorId);

            return View(tracked);
        }

        // GET: Appointments/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var appointment = await _context.Appointments
                .Include(a => a.Patient)
                .Include(a => a.Doctor)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (appointment == null)
            {
                return NotFound();
            }

            return View(appointment);
        }

        // POST: Appointments/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var appointment = await _context.Appointments.FindAsync(id);
            if (appointment != null)
            {
                _context.Appointments.Remove(appointment);
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private bool AppointmentExists(int id)
        {
            return _context.Appointments.Any(e => e.Id == id);
        }

        // POST: Appointments/SendIn/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendIn(int id)
        {
            var appointment = await _context.Appointments
                .Include(a => a.Patient)
                .FirstOrDefaultAsync(a => a.Id == id);

            if (appointment == null)
            {
                return NotFound();
            }

            if (User.IsInRole("Assistant"))
            {
                var assignedDocClaim = User.Claims.FirstOrDefault(c => c.Type == "AssignedDoctorId")?.Value;
                if (!int.TryParse(assignedDocClaim, out int assignedDocId) || appointment.DoctorId != assignedDocId)
                {
                    return Forbid();
                }
            }
            else
            {
                // Only assigned assistants can send in patients to the doctor chamber
                return Forbid();
            }

            var rowsAffected = await _context.Appointments
                .Where(a => a.Id == id && a.Status == AppointmentStatus.Scheduled)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(a => a.Status, AppointmentStatus.InConsultation)
                    .SetProperty(a => a.UpdatedAt, DateTime.UtcNow));

            if (rowsAffected == 0)
            {
                TempData["ErrorMessage"] = "Patient has already been sent in or appointment is not in scheduled state.";
                return RedirectToAction(nameof(Index));
            }

            appointment.Status = AppointmentStatus.InConsultation;
            appointment.UpdatedAt = DateTime.UtcNow;

            var latestVital = await _context.PatientVitals
                .Where(v => v.AppointmentId == appointment.Id)
                .OrderByDescending(v => v.CreatedAt)
                .FirstOrDefaultAsync();

            var payload = new {
                id = appointment.Id,
                patientName = appointment.Patient?.FullName ?? "Unknown",
                uhid = appointment.Patient?.Uhid,
                reason = string.IsNullOrEmpty(appointment.ReasonForVisit) ? "No reason specified." : appointment.ReasonForVisit,
                patientId = appointment.PatientId,
                doctorId = appointment.DoctorId,
                time = appointment.UpdatedAt.ToLocalTime().ToString("hh:mm tt"),
                triage = latestVital?.TriagePriority?.ToString(),
                vitalsSummary = latestVital == null ? null :
                    $"BP {latestVital.SystolicBp}/{latestVital.DiastolicBp} · HR {latestVital.HeartRate} · SpO2 {latestVital.Spo2}% · Temp {latestVital.Temperature}°C · RR {latestVital.RespiratoryRate}"
            };
            
            // Scoped to this doctor's group (which their assistant also joins) -
            // Clients.All used to push every arrival to every connected doctor,
            // regardless of whose patient it was.
            await _hubContext.Clients.Group($"Doctor_{appointment.DoctorId}").SendAsync("PatientSentIn", payload);

            // Fire-and-forget so the assistant isn't stuck waiting several seconds
            // on an AI call just to send a patient in. Runs in its own DI scope
            // because this request's scoped DbContext is disposed as soon as the
            // response returns - reusing _context here would throw once that happens.
            _ = GenerateArrivalSummaryAsync(appointment.PatientId, appointment.DoctorId);

            return RedirectToAction(nameof(Index));
        }

        private async Task GenerateArrivalSummaryAsync(int patientId, int doctorId)
        {
            using var scope = _scopeFactory.CreateScope();
            var aiService = scope.ServiceProvider.GetRequiredService<IClinicalAiService>();
            try
            {
                var suggestion = await aiService.GenerateCaseSummaryAsync(patientId, doctorId, Guid.NewGuid().ToString());

                // Lets an already-open doctor dashboard swap the "generating..." spinner
                // on that patient's card for the real draft without a page reload.
                await _hubContext.Clients.Group($"Doctor_{doctorId}").SendAsync("ArrivalAiReady", new { patientId, suggestionId = suggestion.Id });
            }
            catch (ClinicalAiException ex)
            {
                // No medical history yet, or the AI service is down - the doctor just
                // won't have a pre-generated summary waiting; they can still generate
                // one manually from the patient page.
                _logger.LogInformation("Arrival AI summary skipped for patient {PatientId}: {Reason}", patientId, ex.Reason);
                await _hubContext.Clients.Group($"Doctor_{doctorId}").SendAsync("ArrivalAiFailed", new { patientId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected failure generating arrival AI summary for patient {PatientId}", patientId);
                await _hubContext.Clients.Group($"Doctor_{doctorId}").SendAsync("ArrivalAiFailed", new { patientId });
            }
        }
    }
}
