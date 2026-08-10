using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HospitalManagementSystem.Models;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace HospitalManagementSystem.Controllers
{
    [Authorize(Roles = "Assistant,Admin,Doctor")]
    public class PatientsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public PatientsController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: Patients
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Index(string searchString)
        {
            var patients = from p in _context.Patients
                           select p;

            if (!String.IsNullOrEmpty(searchString))
            {
                var lowerSearch = searchString.ToLower();
                patients = patients.Where(s => (s.FullName != null && s.FullName.ToLower().Contains(lowerSearch))
                                       || s.Uhid.ToLower().Contains(lowerSearch)
                                       || s.ContactInfo.ToString().Contains(lowerSearch));
            }

            return View(await patients.OrderByDescending(p => p.CreatedAt).ToListAsync());
        }

        // GET: Patients/Search
        [HttpGet]
        public async Task<IActionResult> Search(string q)
        {
            var query = _context.Patients.AsQueryable();

            if (!string.IsNullOrEmpty(q))
            {
                var qLower = q.ToLower();
                query = query.Where(p => 
                    p.Uhid.ToLower().Contains(qLower) || 
                    (p.FullName != null && p.FullName.ToLower().Contains(qLower)) || 
                    p.ContactInfo.ToString().Contains(qLower) ||
                    (p.EmergencyContactName != null && p.EmergencyContactName.ToLower().Contains(qLower)));
            }

            var results = await query
                .OrderByDescending(p => p.CreatedAt)
                .Take(20) // Limit results for performance
                .Select(p => new {
                    id = p.Id,
                    text = p.Uhid + " - " + (string.IsNullOrEmpty(p.FullName) ? "Baby of " + p.EmergencyContactName : p.FullName)
                })
                .ToListAsync();

            return Json(new { results });
        }

        // GET: Patients/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var patient = await _context.Patients
                .Include(p => p.RegisteredBy)
                .FirstOrDefaultAsync(m => m.Id == id);
            
            if (patient == null)
            {
                return NotFound();
            }

            return View(patient);
        }

        // GET: Patients/Create
        [Authorize(Roles = "Assistant,Admin")]
        public IActionResult Create()
        {
            return View();
        }

        // POST: Patients/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Assistant,Admin")]
        public async Task<IActionResult> Create([Bind("IsChild,FullName,ContactInfo,DateOfBirth,Gender,BloodGroup,EmergencyContactName,EmergencyContactPhone")] Patient patient)
        {
            // Remove properties that are auto-generated from ModelState validation
            ModelState.Remove("Uhid");
            ModelState.Remove("Admissions");
            
            // Allow BloodGroup to be empty/null if user doesn't provide them
            if (string.IsNullOrEmpty(patient.BloodGroup)) ModelState.Remove("BloodGroup");
            
            if (patient.IsChild)
            {
                if (string.IsNullOrWhiteSpace(patient.EmergencyContactName)) ModelState.AddModelError("EmergencyContactName", "Guardian Name is required for minors.");
                if (!patient.EmergencyContactPhone.HasValue) ModelState.AddModelError("EmergencyContactPhone", "Guardian Phone is required for minors.");
                
                // Name and personal contact info are optional for a child
                if (string.IsNullOrWhiteSpace(patient.FullName)) ModelState.Remove("FullName");
                if (!patient.ContactInfo.HasValue) ModelState.Remove("ContactInfo"); 
            }
            else
            {
                if (string.IsNullOrWhiteSpace(patient.FullName)) ModelState.AddModelError("FullName", "Patient Name is required for adults.");
                if (string.IsNullOrWhiteSpace(patient.EmergencyContactName)) ModelState.AddModelError("EmergencyContactName", "Emergency Contact Name is required.");
                if (!patient.EmergencyContactPhone.HasValue) ModelState.AddModelError("EmergencyContactPhone", "Emergency Contact Phone is required.");
                if (!patient.ContactInfo.HasValue) ModelState.AddModelError("ContactInfo", "Phone Number is required for adults.");
            }

            if (ModelState.IsValid)
            {
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (int.TryParse(userIdClaim, out int userId))
                {
                    patient.RegisteredById = userId;
                }

                // Auto-generate UHID: PT-YYYYMM-XXXX
                string prefix = $"PT-{DateTime.UtcNow:yyyyMM}-";
                
                var lastPatient = await _context.Patients
                    .Where(p => p.Uhid.StartsWith(prefix))
                    .OrderByDescending(p => p.Id)
                    .FirstOrDefaultAsync();

                int nextNumber = 1;
                if (lastPatient != null)
                {
                    string lastNumberStr = lastPatient.Uhid.Substring(prefix.Length);
                    if (int.TryParse(lastNumberStr, out int lastNumber))
                    {
                        nextNumber = lastNumber + 1;
                    }
                }

                patient.Uhid = $"{prefix}{nextNumber:D4}";
                patient.CreatedAt = DateTime.UtcNow;
                patient.UpdatedAt = DateTime.UtcNow;
                
                // PostgreSQL requires all DateTimes to be UTC
                patient.DateOfBirth = DateTime.SpecifyKind(patient.DateOfBirth, DateTimeKind.Utc);

                _context.Add(patient);
                await _context.SaveChangesAsync();
                
                TempData["SuccessMessage"] = $"Patient {patient.FullName} registered successfully! UHID: {patient.Uhid}";
                
                if (User.IsInRole("Assistant"))
                {
                    return RedirectToAction("Index", "Appointments");
                }
                return RedirectToAction(nameof(Index));
            }
            return View(patient);
        }
        // GET: Patients/Edit/5
        [Authorize(Roles = "Assistant,Admin")]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var patient = await _context.Patients.FindAsync(id);
            if (patient == null) return NotFound();
            
            return View(patient);
        }

        // POST: Patients/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Assistant,Admin")]
        public async Task<IActionResult> Edit(int id, [Bind("Id,Uhid,IsChild,FullName,ContactInfo,DateOfBirth,Gender,BloodGroup,EmergencyContactName,EmergencyContactPhone,CreatedAt,RegisteredById")] Patient patient)
        {
            if (id != patient.Id) return NotFound();

            ModelState.Remove("Admissions");
            if (string.IsNullOrEmpty(patient.BloodGroup)) ModelState.Remove("BloodGroup");

            if (patient.IsChild)
            {
                if (string.IsNullOrWhiteSpace(patient.EmergencyContactName)) ModelState.AddModelError("EmergencyContactName", "Guardian Name is required for minors.");
                if (!patient.EmergencyContactPhone.HasValue) ModelState.AddModelError("EmergencyContactPhone", "Guardian Phone is required for minors.");
                
                // Name and personal contact info are optional for a child
                if (string.IsNullOrWhiteSpace(patient.FullName)) ModelState.Remove("FullName");
                if (!patient.ContactInfo.HasValue) ModelState.Remove("ContactInfo"); 
            }
            else
            {
                if (string.IsNullOrWhiteSpace(patient.FullName)) ModelState.AddModelError("FullName", "Patient Name is required for adults.");
                if (string.IsNullOrWhiteSpace(patient.EmergencyContactName)) ModelState.AddModelError("EmergencyContactName", "Emergency Contact Name is required.");
                if (!patient.EmergencyContactPhone.HasValue) ModelState.AddModelError("EmergencyContactPhone", "Emergency Contact Phone is required.");
                if (!patient.ContactInfo.HasValue) ModelState.AddModelError("ContactInfo", "Phone Number is required for adults.");
            }

            if (ModelState.IsValid)
            {
                try
                {
                    patient.DateOfBirth = DateTime.SpecifyKind(patient.DateOfBirth, DateTimeKind.Utc);
                    patient.CreatedAt = DateTime.SpecifyKind(patient.CreatedAt, DateTimeKind.Utc);
                    patient.UpdatedAt = DateTime.UtcNow;

                    _context.Update(patient);
                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = $"Patient {patient.FullName} updated successfully!";
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!PatientExists(patient.Id)) return NotFound();
                    else throw;
                }
                return RedirectToAction(nameof(Index));
            }
            return View(patient);
        }

        // POST: Patients/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var patient = await _context.Patients.FindAsync(id);
            if (patient != null)
            {
                _context.Patients.Remove(patient);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Patient {patient.FullName} deleted successfully!";
            }
            return RedirectToAction(nameof(Index));
        }

        private bool PatientExists(int id)
        {
            return _context.Patients.Any(e => e.Id == id);
        }
    }
}
