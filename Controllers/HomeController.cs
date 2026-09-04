using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
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
        else if (User.IsInRole("Admin"))
        {
            var today = DateTime.UtcNow.Date;

            ViewBag.TotalPatients = await _context.Patients.CountAsync();
            ViewBag.ActiveAdmissions = await _context.Admissions.CountAsync(a => a.DischargeDate == null);

            var totalBeds = await _context.Beds.CountAsync();
            var occupiedBeds = await _context.BedTransfers
                .Where(bt => bt.EndDate == null)
                .Select(bt => bt.BedId)
                .Distinct()
                .CountAsync();
            ViewBag.FreeBeds = totalBeds - occupiedBeds;

            var unpaidBills = await _context.Bills
                .Where(b => b.Status != BillStatus.Paid)
                .ToListAsync();
            ViewBag.UnpaidBillCount = unpaidBills.Count;
            ViewBag.OutstandingAmount = unpaidBills.Sum(b => b.NetTotal - b.PaidAmount);

            ViewBag.TodaysAppointments = await _context.Appointments
                .CountAsync(a => a.AppointmentDatetime >= today && a.AppointmentDatetime < today.AddDays(1));
        }
        return View();
    }

    [AllowAnonymous]
    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    [AllowAnonymous]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
