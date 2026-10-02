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
                .AsQueryable();

            if (User.IsInRole("Doctor"))
            {
                var userIdClaim = User.FindFirst("UserId")?.Value;
                if (int.TryParse(userIdClaim, out int docId))
                {
                    query = query.Where(r => r.DoctorId == docId);
                }
            }

            query = query.OrderByDescending(r => r.RecordedAt);

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
        public IActionResult Create(int? patientId, int? doctorId, int? appointmentId)
        {
            if (appointmentId.HasValue)
            {
                ViewBag.AppointmentId = appointmentId.Value;
                var appointment = _context.Appointments
                    .Include(a => a.Patient)
                    .Include(a => a.Doctor)
                    .FirstOrDefault(a => a.Id == appointmentId.Value);

                if (appointment != null)
                {
                    if (!patientId.HasValue) patientId = appointment.PatientId;
                    if (!doctorId.HasValue) doctorId = appointment.DoctorId;
                    ViewBag.Appointment = appointment;
                }
            }

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

            var model = new MedicalRecord();
            if (appointmentId.HasValue) model.AppointmentId = appointmentId.Value;
            if (patientId.HasValue) model.PatientId = patientId.Value;
            if (doctorId.HasValue) model.DoctorId = doctorId.Value;

            return View(model);
        }

        // POST: MedicalRecords/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("PatientId,DoctorId,AppointmentId,ChiefComplaint,Diagnosis,Treatment")] MedicalRecord record)
        {
            ModelState.Remove("Patient");
            ModelState.Remove("Doctor");
            ModelState.Remove("Appointment");

            if (User.IsInRole("Doctor"))
            {
                var userIdClaim = User.FindFirst("UserId")?.Value;
                if (int.TryParse(userIdClaim, out int currentDoctorId))
                {
                    record.DoctorId = currentDoctorId;
                }
            }

            var patientExists = await _context.Patients.AnyAsync(p => p.Id == record.PatientId);
            var doctorExists = await _context.Users.AnyAsync(u => u.Id == record.DoctorId);

            if (!patientExists) ModelState.AddModelError("PatientId", "Patient is required.");
            if (!doctorExists) ModelState.AddModelError("DoctorId", "Doctor is required.");
            if (string.IsNullOrWhiteSpace(record.Diagnosis)) ModelState.AddModelError("Diagnosis", "Diagnosis is required.");
            if (string.IsNullOrWhiteSpace(record.Treatment)) ModelState.AddModelError("Treatment", "Treatment is required.");

            if (record.AppointmentId.HasValue)
            {
                var appt = await _context.Appointments.FindAsync(record.AppointmentId.Value);
                if (appt == null || appt.PatientId != record.PatientId)
                {
                    ModelState.AddModelError("AppointmentId", "The specified appointment is invalid or does not belong to this patient.");
                }
            }

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

                if (record.AppointmentId.HasValue)
                {
                    return RedirectToAction("Index", "DoctorDashboard");
                }
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

            // Verify clinical care relationship if caller is a Doctor or Assistant
            if (User.IsInRole("Doctor") || User.IsInRole("Assistant"))
            {
                int? effectiveDoctorId = null;
                if (User.IsInRole("Doctor"))
                {
                    var userIdClaim = User.FindFirst("UserId")?.Value;
                    if (int.TryParse(userIdClaim, out int docId)) effectiveDoctorId = docId;
                }
                else if (User.IsInRole("Assistant"))
                {
                    var asstDocClaim = User.FindFirst("AssignedDoctorId")?.Value;
                    if (int.TryParse(asstDocClaim, out int docId)) effectiveDoctorId = docId;
                }

                if (effectiveDoctorId.HasValue)
                {
                    var docId = effectiveDoctorId.Value;
                    if (record.DoctorId != docId)
                    {
                        var hasCareRelationship = await _context.Appointments.AnyAsync(a => a.PatientId == record.PatientId && a.DoctorId == docId)
                            || await _context.Admissions.AnyAsync(a => a.PatientId == record.PatientId && a.AdmittingDoctorId == docId)
                            || await _context.MedicalRecords.AnyAsync(m => m.PatientId == record.PatientId && m.DoctorId == docId)
                            || await _context.Prescriptions.AnyAsync(p => p.PatientId == record.PatientId && p.DoctorId == docId);

                        if (!hasCareRelationship)
                        {
                            return Forbid();
                        }
                    }
                }
                else
                {
                    return Forbid();
                }
            }

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
