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
            // Show today's active queue
            var today = DateTime.UtcNow.Date;
            var query = _context.Appointments
                .Include(a => a.Patient)
                .Include(a => a.Doctor)
                .Where(a => a.AppointmentDatetime >= today && a.AppointmentDatetime < today.AddDays(1))
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
            return View();
        }

        // POST: Appointments/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Id,PatientId,DoctorId,ReasonForVisit,Status")] Appointment appointment)
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
            }

            if (ModelState.IsValid)
            {
                appointment.AppointmentDatetime = DateTime.UtcNow;
                appointment.EndTime = DateTime.UtcNow.AddMinutes(15);
                appointment.CreatedAt = DateTime.UtcNow;
                appointment.UpdatedAt = DateTime.UtcNow;

                _context.Add(appointment);
                await _context.SaveChangesAsync();
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
            
            // Don't format time for view since we removed the input fields

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
        public async Task<IActionResult> Edit(int id, [Bind("Id,PatientId,DoctorId,ReasonForVisit,Status")] Appointment appointment)
        {
            if (id != appointment.Id)
            {
                return NotFound();
            }

            ModelState.Remove("Patient");
            ModelState.Remove("Doctor");

            if (ModelState.IsValid)
            {
                try
                {
                    var existing = await _context.Appointments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id);
                    if (existing != null)
                    {
                        appointment.AppointmentDatetime = existing.AppointmentDatetime;
                        appointment.EndTime = existing.EndTime;
                        appointment.CreatedAt = existing.CreatedAt;
                        appointment.UpdatedAt = DateTime.UtcNow;

                        _context.Update(appointment);
                        await _context.SaveChangesAsync();
                    }
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!AppointmentExists(appointment.Id))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
                return RedirectToAction(nameof(Index));
            }
            var doctors = _context.Users
                .Include(u => u.Role)
                .Where(u => u.Role.RoleName == "Doctor")
                .Select(u => new { u.Id, u.FullName })
                .ToList();
            ViewData["DoctorId"] = new SelectList(doctors, "Id", "FullName", appointment.DoctorId);
            return View(appointment);
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
                
            if (appointment != null && appointment.Status == AppointmentStatus.Scheduled)
            {
                appointment.Status = AppointmentStatus.InConsultation;
                appointment.UpdatedAt = DateTime.UtcNow;
                _context.Update(appointment);
                await _context.SaveChangesAsync();

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
            }
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
