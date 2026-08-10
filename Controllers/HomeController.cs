using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Hospital_Management_System.Models;
using HospitalManagementSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace Hospital_Management_System.Controllers;

public class HomeController : Controller
{
    private readonly ApplicationDbContext _context;

    public HomeController(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        if (User.IsInRole("Assistant"))
        {
            var doctorIdClaim = User.Claims.FirstOrDefault(c => c.Type == "AssignedDoctorId")?.Value;
            if (int.TryParse(doctorIdClaim, out int docId))
            {
                var today = DateTime.UtcNow.Date;
                var appointments = await _context.Appointments
                    .Include(a => a.Patient)
                    .Include(a => a.Doctor)
                    .Where(a => a.DoctorId == docId && a.AppointmentDatetime >= today && a.AppointmentDatetime < today.AddDays(1))
                    .ToListAsync();

                ViewBag.TotalScheduled = appointments.Count;
                ViewBag.Waiting = appointments.Count(a => a.Status == AppointmentStatus.Scheduled);
                ViewBag.Completed = appointments.Count(a => a.Status == AppointmentStatus.Completed);
                
                var inProgress = appointments.FirstOrDefault(a => a.Status == AppointmentStatus.InConsultation);
                ViewBag.InProgressPatient = inProgress != null ? inProgress.Patient.FullName : "None";
                
                var doctor = await _context.Users.FindAsync(docId);
                ViewBag.DoctorName = doctor?.FullName ?? "Unknown";
            }
        }
        return View();
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }

    [HttpGet("/ClearAdmissions")]
    public async Task<IActionResult> ClearAdmissions([FromServices] HospitalManagementSystem.Models.ApplicationDbContext context)
    {
        await Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.ExecuteSqlRawAsync(context.Database, "TRUNCATE TABLE \"BedTransfers\", \"Admissions\" CASCADE;");
        return Content("Cleared.");
    }
}
