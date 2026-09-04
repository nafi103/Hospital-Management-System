using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HospitalManagementSystem.Models;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace HospitalManagementSystem.Controllers
{
    [Authorize(Roles = "Assistant,Admin,Doctor,Receptionist")]
    public class PatientsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public PatientsController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: Patients
        [Authorize(Roles = "Admin,Receptionist,Doctor")]
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
                    (p.IsChild && p.EmergencyContactName != null && p.EmergencyContactName.ToLower().Contains(qLower)));
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
                .Include(p => p.Allergies)
                .FirstOrDefaultAsync(m => m.Id == id);
            
            if (patient == null)
            {
                return NotFound();
            }

            ViewBag.KnownAllergens = await _context.Medicines
                .Select(m => m.GenericName)
                .Distinct()
                .OrderBy(g => g)
                .ToListAsync();

            ViewBag.AiSuggestions = await _context.AiSuggestions
                .Include(s => s.ReviewedBy)
                .Where(s => s.PatientId == id)
                .OrderByDescending(s => s.CreatedAt)
                .Take(5)
                .ToListAsync();

            ViewBag.HasMedicalRecords = await _context.MedicalRecords.AnyAsync(r => r.PatientId == id);

            return View(patient);
        }

        // GET: Patients/Create
        [Authorize(Roles = "Assistant,Admin,Receptionist")]
        public IActionResult Create()
        {
            return View();
        }

        // POST: Patients/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Assistant,Admin,Receptionist")]
        public async Task<IActionResult> Create([Bind("IsChild,FullName,ContactInfo,DateOfBirth,Gender,BloodGroup,EmergencyContactName,EmergencyContactPhone")] Patient patient, bool issuePortalLogin = false)
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
                var userIdClaim = User.FindFirst("UserId")?.Value;
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

                var successMessage = $"Patient {patient.FullName} registered successfully! UHID: {patient.Uhid}";

                // A child has no identity of their own to log in as - the checkbox only
                // applies to adult patients, silently ignored otherwise.
                if (issuePortalLogin && !patient.IsChild)
                {
                    var tempPassword = await IssuePortalLoginAsync(patient);
                    successMessage += $" Portal login issued - username: {patient.Uhid}, temporary password: {tempPassword} (shown once - share it with the patient now).";
                }

                TempData["SuccessMessage"] = successMessage;

                return RedirectAfterPatientSave();
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
        public async Task<IActionResult> Edit(int id, [Bind("Id,Uhid,IsChild,FullName,ContactInfo,DateOfBirth,Gender,BloodGroup,EmergencyContactName,EmergencyContactPhone,CreatedAt,RegisteredById,UserId")] Patient patient)
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
                return RedirectAfterPatientSave();
            }
            return View(patient);
        }

        // POST: Patients/IssueLogin/5 - lets a receptionist grant a portal login to a
        // patient who was registered before this feature existed (or who opted out at
        // registration time).
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Assistant,Admin,Receptionist")]
        public async Task<IActionResult> IssueLogin(int id)
        {
            var patient = await _context.Patients.FindAsync(id);
            if (patient == null) return NotFound();

            if (patient.IsChild)
            {
                TempData["ErrorMessage"] = "A child patient has no identity of their own to issue a portal login for.";
                return RedirectToAction(nameof(Details), new { id });
            }
            if (patient.UserId.HasValue)
            {
                TempData["ErrorMessage"] = "This patient already has a portal login.";
                return RedirectToAction(nameof(Details), new { id });
            }

            var tempPassword = await IssuePortalLoginAsync(patient);
            TempData["SuccessMessage"] = $"Portal login issued - username: {patient.Uhid}, temporary password: {tempPassword} (shown once - share it with the patient now).";
            return RedirectToAction(nameof(Details), new { id });
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

        // An Assistant can't reach Patients/Index (Admin/Receptionist only), so sending
        // them there after a save 403s. Route them back to their own queue instead;
        // everyone else who can reach this controller can also reach Index.
        private IActionResult RedirectAfterPatientSave()
        {
            if (User.IsInRole("Assistant"))
            {
                return RedirectToAction("Index", "Appointments");
            }
            return RedirectToAction(nameof(Index));
        }

        // Creates the patient's portal login: username is their UHID (unique, already
        // hospital-issued, easy for them to remember), a random temporary password shown
        // exactly once by the caller, and the Patient role. Persists patient.UserId so the
        // link survives past this request.
        private async Task<string> IssuePortalLoginAsync(Patient patient)
        {
            var patientRoleId = await _context.Roles
                .Where(r => r.RoleName == "Patient")
                .Select(r => r.Id)
                .FirstAsync();

            var tempPassword = GenerateTempPassword();
            var now = DateTime.UtcNow;
            var portalUser = new User
            {
                RoleId = patientRoleId,
                Username = patient.Uhid,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(tempPassword),
                FullName = patient.FullName ?? patient.Uhid,
                Category = "Patient",
                CreatedAt = now,
                UpdatedAt = now
            };
            _context.Users.Add(portalUser);
            await _context.SaveChangesAsync();

            patient.UserId = portalUser.Id;
            await _context.SaveChangesAsync();

            return tempPassword;
        }

        private static string GenerateTempPassword()
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
            var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(10);
            var result = new char[10];
            for (var i = 0; i < result.Length; i++)
            {
                result[i] = chars[bytes[i] % chars.Length];
            }
            return new string(result);
        }
    }
}
