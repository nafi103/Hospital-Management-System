using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using HospitalManagementSystem.Models;

namespace HospitalManagementSystem.Controllers
{
    [Authorize(Roles = "Doctor,Admin")]
    public class MedicalRecordsController : Controller
    {
        private const int PageSize = 20;

        private readonly ApplicationDbContext _context;

        public MedicalRecordsController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: MedicalRecords
        public async Task<IActionResult> Index(int page = 1)
        {
            if (page < 1) page = 1;

            var query = _context.MedicalRecords
                .Include(r => r.Patient)
                .Include(r => r.Doctor)
                .OrderByDescending(r => r.RecordedAt);

            var totalRecords = await query.CountAsync();
            var totalPages = Math.Max(1, (int)Math.Ceiling(totalRecords / (double)PageSize));
            page = Math.Min(page, totalPages);

            var records = await query
                .Skip((page - 1) * PageSize)
                .Take(PageSize)
                .ToListAsync();

            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;

            return View(records);
        }

        // GET: MedicalRecords/Create
        public IActionResult Create(int? patientId, int? doctorId)
        {
            var doctors = _context.Users
                .Include(u => u.Role)
                .Where(u => u.Role.RoleName == "Doctor")
                .Select(u => new { u.Id, u.FullName })
                .ToList();
            ViewData["DoctorId"] = new SelectList(doctors, "Id", "FullName", doctorId);

            if (doctorId.HasValue)
            {
                var doctor = _context.Users.Find(doctorId.Value);
                if (doctor != null)
                {
                    ViewBag.PreselectedDoctorId = doctor.Id;
                    ViewBag.PreselectedDoctorName = doctor.FullName;
                }
            }

            if (patientId.HasValue)
            {
                var patient = _context.Patients.Find(patientId.Value);
                if (patient != null)
                {
                    ViewBag.PreselectedPatientId = patient.Id;
                    ViewBag.PreselectedPatientUhid = patient.Uhid;
                    ViewBag.PreselectedPatientName = patient.FullName;
                }
            }

            return View();
        }

        // POST: MedicalRecords/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("PatientId,DoctorId,ChiefComplaint,Diagnosis,Treatment")] MedicalRecord record)
        {
            ModelState.Remove("Patient");
            ModelState.Remove("Doctor");
            ModelState.Remove("Appointment");

            var patientExists = await _context.Patients.AnyAsync(p => p.Id == record.PatientId);
            var doctorExists = await _context.Users.AnyAsync(u => u.Id == record.DoctorId);

            if (!patientExists) ModelState.AddModelError("PatientId", "Patient is required.");
            if (!doctorExists) ModelState.AddModelError("DoctorId", "Doctor is required.");
            if (string.IsNullOrWhiteSpace(record.Diagnosis)) ModelState.AddModelError("Diagnosis", "Diagnosis is required.");
            if (string.IsNullOrWhiteSpace(record.Treatment)) ModelState.AddModelError("Treatment", "Treatment is required.");

            if (ModelState.IsValid)
            {
                record.RecordedAt = DateTime.UtcNow;
                record.CreatedAt = DateTime.UtcNow;
                record.UpdatedAt = DateTime.UtcNow;

                _context.MedicalRecords.Add(record);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "Medical record saved successfully.";
                TempData["CrossLinkController"] = "Prescriptions";
                TempData["CrossLinkLabel"] = "Write prescription for this visit";
                TempData["CrossLinkPatientId"] = record.PatientId;
                TempData["CrossLinkDoctorId"] = record.DoctorId;
                return RedirectToAction(nameof(Index));
            }

            var doctors = _context.Users.Include(u => u.Role).Where(u => u.Role.RoleName == "Doctor").Select(u => new { u.Id, u.FullName }).ToList();
            ViewData["DoctorId"] = new SelectList(doctors, "Id", "FullName", record.DoctorId);
            return View(record);
        }

        // GET: MedicalRecords/Details/5
        public async Task<IActionResult> Details(int id)
        {
            var record = await _context.MedicalRecords
                .Include(r => r.Patient)
                .Include(r => r.Doctor)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (record == null) return NotFound();
            return View(record);
        }

        // GET: MedicalRecords/Delete/5
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(int id)
        {
            var record = await _context.MedicalRecords
                .Include(r => r.Patient)
                .Include(r => r.Doctor)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (record == null) return NotFound();
            return View(record);
        }

        // POST: MedicalRecords/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var record = await _context.MedicalRecords.FindAsync(id);
            if (record != null)
            {
                _context.MedicalRecords.Remove(record);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Medical record deleted successfully.";
            }
            return RedirectToAction(nameof(Index));
        }
    }
}
