using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using HospitalManagementSystem.Models;
using HospitalManagementSystem.Hubs;

namespace HospitalManagementSystem.Controllers
{
    [Authorize(Roles = "Doctor")]
    public class DoctorDashboardController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IHubContext<NotificationHub> _hubContext;

        public DoctorDashboardController(ApplicationDbContext context, IHubContext<NotificationHub> hubContext)
        {
            _context = context;
            _hubContext = hubContext;
        }

        // GET: DoctorDashboard
        public async Task<IActionResult> Index()
        {
            // Fetch appointments that are currently InConsultation
            var activeConsultations = await _context.Appointments
                .Include(a => a.Patient)
                .Include(a => a.Doctor)
                .Where(a => a.Status == AppointmentStatus.InConsultation)
                .OrderBy(a => a.UpdatedAt) // Oldest sent in first
                .ToListAsync();

            return View(activeConsultations);
        }

        // POST: DoctorDashboard/MarkCompleted/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkCompleted(int id)
        {
            var appointment = await _context.Appointments
                .Include(a => a.Patient)
                .FirstOrDefaultAsync(a => a.Id == id);

            if (appointment != null && appointment.Status == AppointmentStatus.InConsultation)
            {
                appointment.Status = AppointmentStatus.Completed;
                appointment.UpdatedAt = DateTime.UtcNow;
                _context.Update(appointment);
                await _context.SaveChangesAsync();

                // Notify Assistant
                await _hubContext.Clients.Group($"Doctor_{appointment.DoctorId}")
                    .SendAsync("ReceiveNotification", appointment.Id, appointment.Patient?.FullName ?? "Unknown Patient");
            }
            return RedirectToAction(nameof(Index));
        }

        // GET: DoctorDashboard/MyAssistant
        public async Task<IActionResult> MyAssistant()
        {
            var doctorIdClaim = User.Claims.FirstOrDefault(c => c.Type == "UserId")?.Value;
            if (int.TryParse(doctorIdClaim, out int docId))
            {
                var assistants = await _context.Users
                    .Where(u => u.AssignedDoctorId == docId && u.Role.RoleName == "Assistant")
                    .ToListAsync();
                
                return View(assistants);
            }
            return RedirectToAction(nameof(Index));
        }

        // POST: DoctorDashboard/CreateAssistant
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateAssistant(string username, string password, string fullName)
        {
            var doctorIdClaim = User.Claims.FirstOrDefault(c => c.Type == "UserId")?.Value;
            if (int.TryParse(doctorIdClaim, out int docId))
            {
                if (await _context.Users.AnyAsync(u => u.Username == username))
                {
                    TempData["Error"] = "Username is already taken.";
                    return RedirectToAction(nameof(MyAssistant));
                }

                var role = await _context.Roles.FirstOrDefaultAsync(r => r.RoleName == "Assistant");
                
                var assistant = new User
                {
                    Username = username,
                    Password = password, // Note: storing plaintext for demonstration purposes
                    FullName = fullName,
                    RoleId = role.Id,
                    AssignedDoctorId = docId,
                    Category = "Staff",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                _context.Users.Add(assistant);
                await _context.SaveChangesAsync();
                
                TempData["Success"] = "Assistant created successfully!";
            }
            return RedirectToAction(nameof(MyAssistant));
        }

        // POST: DoctorDashboard/FireAssistant/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> FireAssistant(int id)
        {
            var doctorIdClaim = User.Claims.FirstOrDefault(c => c.Type == "UserId")?.Value;
            if (int.TryParse(doctorIdClaim, out int docId))
            {
                var assistant = await _context.Users.FindAsync(id);
                if (assistant != null && assistant.AssignedDoctorId == docId)
                {
                    _context.Users.Remove(assistant);
                    await _context.SaveChangesAsync();
                    TempData["Success"] = "Assistant account deleted (fired) successfully!";
                }
            }
            return RedirectToAction(nameof(MyAssistant));
        }

        // POST: DoctorDashboard/UpdateAssistant/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateAssistant(int id, string fullName, string username, string password)
        {
            var doctorIdClaim = User.Claims.FirstOrDefault(c => c.Type == "UserId")?.Value;
            if (int.TryParse(doctorIdClaim, out int docId))
            {
                var assistant = await _context.Users.FindAsync(id);
                if (assistant != null && assistant.AssignedDoctorId == docId)
                {
                    if (await _context.Users.AnyAsync(u => u.Username == username && u.Id != id))
                    {
                        TempData["Error"] = "Username is already taken by another user.";
                        return RedirectToAction(nameof(MyAssistant));
                    }

                    assistant.FullName = fullName;
                    assistant.Username = username;
                    if (!string.IsNullOrEmpty(password))
                    {
                        assistant.Password = password;
                    }
                    assistant.UpdatedAt = DateTime.UtcNow;

                    _context.Users.Update(assistant);
                    await _context.SaveChangesAsync();
                    TempData["Success"] = "Assistant credentials updated successfully!";
                }
            }
            return RedirectToAction(nameof(MyAssistant));
        }
    }
}
