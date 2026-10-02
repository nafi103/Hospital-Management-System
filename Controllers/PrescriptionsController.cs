using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using HospitalManagementSystem.Models;
using HospitalManagementSystem.Services;

namespace HospitalManagementSystem.Controllers
{
    // Multiple [Authorize(Roles=...)] attributes on the same request (class + method) are
    // ANDed together, not overridden - a narrower class-level list can't be widened by a
    // broader one on a single action. So the class level is the widest set anything here
    // needs (staff + Patient, for Print), and every action that must stay staff-only
    // narrows back down with its own [Authorize] - Create and Dispense already did this
    // for their doctor/pharmacist-only rules; Index and Details need the same treatment
    // now that Patient is in the class-level set.
    [Authorize(Roles = "Doctor,Pharmacist,Admin,Patient")]
    public class PrescriptionsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public PrescriptionsController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: Prescriptions
        [Authorize(Roles = "Doctor,Pharmacist,Admin")]
        public async Task<IActionResult> Index()
        {
            var prescriptions = await _context.Prescriptions
                .Include(p => p.Patient)
                .Include(p => p.Doctor)
                .OrderBy(p => p.Status)
                .ThenByDescending(p => p.CreatedAt)
                .ToListAsync();
            return View(prescriptions);
        }

        // GET: Prescriptions/Print/5 - the only action here a Patient can reach, and only
        // for their own prescription (inherits the class-level policy; no narrowing here).
        public async Task<IActionResult> Print(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var prescription = await _context.Prescriptions
                .Include(p => p.Patient)
                .Include(p => p.Doctor)
                .Include(p => p.PrescriptionItems)
                    .ThenInclude(i => i.Medicine)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (prescription == null)
            {
                return NotFound();
            }

            if (User.IsInRole("Patient"))
            {
                var userIdClaim = User.FindFirst("UserId")?.Value;
                var isOwner = int.TryParse(userIdClaim, out int userId)
                    && await _context.Patients.AnyAsync(p => p.Id == prescription.PatientId && p.UserId == userId);
                if (!isOwner)
                {
                    return Forbid();
                }
            }

            return View(prescription);
        }

        // GET: Prescriptions/Create
        [Authorize(Roles = "Doctor")]
        public IActionResult Create(int? patientId)
        {
            var userIdClaim = User.Claims.FirstOrDefault(c => c.Type == "UserId")?.Value;
            if (userIdClaim != null)
            {
                int currentDoctorId = int.Parse(userIdClaim);
                var doctor = _context.Users.Find(currentDoctorId);
                if (doctor != null)
                {
                    ViewBag.PreselectedDoctorId = doctor.Id;
                    ViewBag.PreselectedDoctorName = doctor.FullName;
                }
            }
            
            // Every medicine is searchable here, including zero-stock ones - the search UI
            // already renders a red "Stock: 0" badge for those (see appendMedicineItem in
            // Create.cshtml). Hiding them entirely would make PrescriptionSafetyChecker's
            // stock/substitution warning unreachable: a doctor can never be warned about
            // prescribing something they were never allowed to select in the first place.
            var medicines = _context.Medicines
                .Select(m => new {
                    m.Id,
                    DisplayName = m.Name + " (৳" + m.UnitPrice.ToString("0.00") + ")",
                    m.UnitPrice,
                    m.StockQuantity,
                    m.GenericName
                }).ToList();

            ViewBag.MedicinesList = medicines;

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

        // POST: Prescriptions/CheckSafety - called via fetch() before the real submit, so
        // the doctor sees duplicate-therapy/allergy/stock/pediatric-dose warnings and can
        // acknowledge or go back and edit, without losing the in-progress form. This is a
        // convenience for the doctor, not the enforcement boundary - Create (below) re-runs
        // the same check server-side against whatever was actually submitted, because a
        // client-side-only check can be bypassed or simply skipped.
        public class SafetyCheckItemDto
        {
            public int MedicineId { get; set; }
            public int Quantity { get; set; }
            public string DoseUnit { get; set; } = "Tablet";
        }

        // Bound from a regular form-encoded POST (not [FromBody] JSON) so the standard
        // antiforgery field validation applies the same way it does everywhere else in
        // this app - see wwwroot/js/ai-stream.js for the same convention. The item list
        // travels as one JSON-encoded form field rather than indexed form keys, since it's
        // only ever read here, never model-bound as a page's real submission.
        private static readonly JsonSerializerOptions CamelCaseJsonOptions = new() { PropertyNameCaseInsensitive = true };

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Doctor")]
        public async Task<IActionResult> CheckSafety(int patientId, string? itemsJson)
        {
            List<SafetyCheckItemDto>? dtos;
            try
            {
                // The browser sends camelCase (medicineId, quantity, doseUnit); Deserialize
                // is case-sensitive by default and would otherwise silently bind every
                // property to its default value instead of throwing - PropertyNameCaseInsensitive
                // is required here, not optional polish.
                dtos = string.IsNullOrWhiteSpace(itemsJson)
                    ? null
                    : JsonSerializer.Deserialize<List<SafetyCheckItemDto>>(itemsJson, CamelCaseJsonOptions);
            }
            catch (JsonException)
            {
                dtos = null;
            }

            if (patientId == 0 || dtos == null || dtos.Count == 0)
            {
                return Json(new { warnings = Array.Empty<object>() });
            }

            var items = dtos
                .Select(i => new SafetyCheckItem(i.MedicineId, i.Quantity, ParseDoseUnit(i.DoseUnit)))
                .ToList();
            var warnings = await ComputeSafetyWarningsAsync(patientId, items);

            return Json(new
            {
                warnings = warnings.Select(w => new { category = w.Category, severity = w.Severity.ToString(), message = w.Message })
            });
        }

        private static DoseUnit ParseDoseUnit(string value) => Enum.TryParse<DoseUnit>(value, out var unit) ? unit : DoseUnit.Tablet;

        private async Task<List<ActiveMedicationInfo>> GetActiveMedicationsAsync(int patientId)
        {
            var now = DateTime.UtcNow;

            var dispensedPrescriptions = await _context.Prescriptions
                .AsNoTracking()
                .Where(p => p.PatientId == patientId && p.Status == PrescriptionStatus.Dispensed)
                .Include(p => p.PrescriptionItems)
                    .ThenInclude(pi => pi.Medicine)
                .OrderByDescending(p => p.DispensedAt ?? p.CreatedAt)
                .Take(20)
                .ToListAsync();

            var activeMeds = new List<ActiveMedicationInfo>();

            foreach (var p in dispensedPrescriptions)
            {
                var startDate = p.DispensedAt ?? p.CreatedAt;
                foreach (var item in p.PrescriptionItems)
                {
                    if (item.Medicine == null) continue;

                    var duration = item.DurationDays ?? 30;
                    var endDate = startDate.AddDays(duration);

                    if (endDate >= now)
                    {
                        activeMeds.Add(new ActiveMedicationInfo(
                            item.MedicineId,
                            item.Medicine.Name,
                            item.Medicine.GenericName,
                            startDate,
                            item.DurationDays,
                            p.Id
                        ));
                    }
                }
            }

            return activeMeds;
        }

        private async Task<List<SafetyWarning>> ComputeSafetyWarningsAsync(int patientId, List<SafetyCheckItem> items)
        {
            var patient = await _context.Patients.FindAsync(patientId);
            if (patient == null || items.Count == 0)
            {
                return new List<SafetyWarning>();
            }

            var allergies = await _context.PatientAllergies
                .Where(a => a.PatientId == patientId)
                .Select(a => new SafetyCheckAllergy(a.Substance, a.AllergenGenericName, a.Severity))
                .ToListAsync();

            var allMedicines = await _context.Medicines
                .Select(m => new MedicineInfo(m.Id, m.Name, m.GenericName, m.Strength, m.StockQuantity))
                .ToListAsync();

            var activeMedications = await GetActiveMedicationsAsync(patientId);

            return PrescriptionSafetyChecker.Check(patient.IsChild, allergies, items, allMedicines, activeMedications);
        }

        // POST: Prescriptions/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Doctor")]
        public async Task<IActionResult> Create([Bind("PatientId,DoctorId,Notes,ChiefComplaints,Diagnosis,SafetyOverrideReason")] Prescription prescription, List<PrescriptionItem> PrescriptionItems)
        {
            ModelState.Remove("Patient");
            ModelState.Remove("Doctor");
            ModelState.Remove("PrescriptionItems");

            if (ModelState.IsValid && PrescriptionItems != null && PrescriptionItems.Count > 0)
            {
                // Re-run the safety check server-side against what was actually submitted -
                // never trust a client-computed warning list for the audit trail, and never
                // trust that the client-side check even ran.
                var safetyItems = PrescriptionItems.Select(i => new SafetyCheckItem(i.MedicineId, i.Quantity, i.DoseUnit)).ToList();
                var warnings = await ComputeSafetyWarningsAsync(prescription.PatientId, safetyItems);

                if (warnings.Count > 0 && string.IsNullOrWhiteSpace(prescription.SafetyOverrideReason))
                {
                    ModelState.AddModelError("", "Safety warnings were raised for this prescription. Review and acknowledge them before saving.");
                }
                else
                {
                    prescription.Status = PrescriptionStatus.PendingPharmacy;
                    prescription.CreatedAt = DateTime.UtcNow;
                    prescription.UpdatedAt = DateTime.UtcNow;
                    prescription.SafetyWarningsJson = warnings.Count > 0
                        ? JsonSerializer.Serialize(warnings.Select(w => new { w.Category, Severity = w.Severity.ToString(), w.Message }))
                        : null;
                    if (warnings.Count == 0)
                    {
                        prescription.SafetyOverrideReason = null;
                    }

                    foreach (var item in PrescriptionItems)
                    {
                        var medicine = await _context.Medicines.FindAsync(item.MedicineId);
                        if (medicine != null)
                        {
                            item.UnitPrice = medicine.UnitPrice; // Lock in the price at time of prescribing
                            prescription.PrescriptionItems.Add(item);
                        }
                    }

                    _context.Add(prescription);
                    await _context.SaveChangesAsync();

                    TempData["SuccessMessage"] = warnings.Count > 0
                        ? $"Prescription created and sent to pharmacy ({warnings.Count} safety warning(s) reviewed and overridden)."
                        : "Prescription created and sent to pharmacy.";
                    TempData["CrossLinkController"] = "MedicalRecords";
                    TempData["CrossLinkLabel"] = "Add medical record for this visit";
                    TempData["CrossLinkPatientId"] = prescription.PatientId;
                    TempData["CrossLinkDoctorId"] = prescription.DoctorId;
                    return RedirectToAction(nameof(Index));
                }
            }

            if (PrescriptionItems == null || PrescriptionItems.Count == 0)
            {
                ModelState.AddModelError("", "You must add at least one medicine to the prescription.");
            }

            var userIdClaim = User.Claims.FirstOrDefault(c => c.Type == "UserId")?.Value;
            if (userIdClaim != null)
            {
                int currentDoctorId = int.Parse(userIdClaim);
                var doctor = _context.Users.Find(currentDoctorId);
                if (doctor != null)
                {
                    ViewBag.PreselectedDoctorId = doctor.Id;
                    ViewBag.PreselectedDoctorName = doctor.FullName;
                }
            }

            var medicines = _context.Medicines.Select(m => new { m.Id, DisplayName = m.Name + " (৳" + m.UnitPrice.ToString("0.00") + ")", m.UnitPrice, m.StockQuantity, m.GenericName }).ToList();
            ViewBag.MedicinesList = medicines;

            return View(prescription);
        }

        // GET: Prescriptions/Details/5
        [Authorize(Roles = "Doctor,Pharmacist,Admin")]
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var prescription = await _context.Prescriptions
                .Include(p => p.Patient)
                .Include(p => p.Doctor)
                .Include(p => p.DispensedBy)
                .Include(p => p.PrescriptionItems)
                    .ThenInclude(pi => pi.Medicine)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (prescription == null) return NotFound();

            ViewBag.PatientInstructionSuggestions = await _context.AiSuggestions
                .Include(s => s.ReviewedBy)
                .Where(s => s.SuggestionType == AiSuggestionType.PatientInstructions && s.TargetEntityId == prescription.Id)
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync();

            return View(prescription);
        }

        // GET: Prescriptions/PrintPatientInstructions/5 (AiSuggestion id, not PrescriptionId)
        // Printable only once a doctor has Accepted or Edited the draft - Pending/Rejected
        // sheets never reach a patient's hands. Inherits the class-level [Authorize] policy.
        public async Task<IActionResult> PrintPatientInstructions(int suggestionId)
        {
            var suggestion = await _context.AiSuggestions.FirstOrDefaultAsync(s => s.Id == suggestionId);
            if (suggestion == null || suggestion.SuggestionType != AiSuggestionType.PatientInstructions)
            {
                return NotFound();
            }

            if (suggestion.Verdict == AiSuggestionVerdict.Pending || suggestion.Verdict == AiSuggestionVerdict.Rejected)
            {
                TempData["ErrorMessage"] = "This instruction sheet must be reviewed and accepted before it can be printed.";
                return RedirectToAction(nameof(Details), new { id = suggestion.TargetEntityId });
            }

            var prescription = await _context.Prescriptions
                .Include(p => p.Patient)
                .Include(p => p.Doctor)
                .FirstOrDefaultAsync(p => p.Id == suggestion.TargetEntityId);

            if (prescription == null) return NotFound();

            if (User.IsInRole("Patient"))
            {
                var userIdClaim = User.FindFirst("UserId")?.Value;
                var isOwner = int.TryParse(userIdClaim, out int userId)
                    && await _context.Patients.AnyAsync(p => p.Id == prescription.PatientId && p.UserId == userId);
                if (!isOwner)
                {
                    return Forbid();
                }
            }

            ViewBag.Suggestion = suggestion;
            return View(prescription);
        }

        // POST: Prescriptions/Dispense/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Pharmacist")]
        public async Task<IActionResult> Dispense(int id)
        {
            int? pharmacistUserId = null;
            var userIdClaim = User.Claims.FirstOrDefault(c => c.Type == "UserId")?.Value;
            if (int.TryParse(userIdClaim, out int uid))
            {
                pharmacistUserId = uid;
            }

            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                if (_context.Database.IsRelational())
                {
                    // Relational path: direct atomic SQL ExecuteUpdate with database-level concurrency locking
                    var rowsAffected = await _context.Prescriptions
                        .Where(p => p.Id == id && p.Status == PrescriptionStatus.PendingPharmacy)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(p => p.Status, PrescriptionStatus.Dispensed)
                            .SetProperty(p => p.DispensedById, pharmacistUserId)
                            .SetProperty(p => p.DispensedAt, DateTime.UtcNow)
                            .SetProperty(p => p.UpdatedAt, DateTime.UtcNow));

                    if (rowsAffected == 0)
                    {
                        await transaction.RollbackAsync();
                        var existing = await _context.Prescriptions.FindAsync(id);
                        if (existing == null)
                        {
                            return NotFound();
                        }
                        if (existing.Status == PrescriptionStatus.Dispensed)
                        {
                            TempData["ErrorMessage"] = "This prescription has already been dispensed.";
                        }
                        else
                        {
                            TempData["ErrorMessage"] = "Prescription is not ready for pharmacy fulfillment.";
                        }
                        return RedirectToAction(nameof(Details), new { id });
                    }

                    var items = await _context.PrescriptionItems
                        .Include(pi => pi.Medicine)
                        .Where(pi => pi.PrescriptionId == id)
                        .ToListAsync();

                    foreach (var item in items)
                    {
                        var updated = await _context.Medicines
                            .Where(m => m.Id == item.MedicineId && m.StockQuantity >= item.Quantity)
                            .ExecuteUpdateAsync(m => m
                                .SetProperty(x => x.StockQuantity, x => x.StockQuantity - item.Quantity)
                                .SetProperty(x => x.UpdatedAt, DateTime.UtcNow));

                        if (updated == 0)
                        {
                            await transaction.RollbackAsync();
                            var medName = item.Medicine?.Name ?? $"Medicine #{item.MedicineId}";
                            var currentStock = await _context.Medicines
                                .Where(m => m.Id == item.MedicineId)
                                .Select(m => m.StockQuantity)
                                .FirstOrDefaultAsync();
                            TempData["ErrorMessage"] = $"Insufficient stock for {medName}. Requested: {item.Quantity}, Available: {currentStock}. Dispensing cancelled.";
                            return RedirectToAction(nameof(Details), new { id });
                        }
                    }

                    await transaction.CommitAsync();
                }
                else
                {
                    // Non-relational / in-memory test fallback: validate all conditions before mutating
                    var existing = await _context.Prescriptions.FindAsync(id);
                    if (existing == null)
                    {
                        await transaction.RollbackAsync();
                        return NotFound();
                    }

                    if (existing.Status == PrescriptionStatus.Dispensed)
                    {
                        await transaction.RollbackAsync();
                        TempData["ErrorMessage"] = "This prescription has already been dispensed.";
                        return RedirectToAction(nameof(Details), new { id });
                    }

                    if (existing.Status != PrescriptionStatus.PendingPharmacy)
                    {
                        await transaction.RollbackAsync();
                        TempData["ErrorMessage"] = "Prescription is not ready for pharmacy fulfillment.";
                        return RedirectToAction(nameof(Details), new { id });
                    }

                    var items = await _context.PrescriptionItems
                        .Include(pi => pi.Medicine)
                        .Where(pi => pi.PrescriptionId == id)
                        .ToListAsync();

                    // Pre-flight check: ensure every item has sufficient stock before making any changes
                    foreach (var item in items)
                    {
                        var medicine = await _context.Medicines.FindAsync(item.MedicineId);
                        if (medicine == null || medicine.StockQuantity < item.Quantity)
                        {
                            await transaction.RollbackAsync();
                            var medName = medicine?.Name ?? item.Medicine?.Name ?? $"Medicine #{item.MedicineId}";
                            var currentStock = medicine?.StockQuantity ?? 0;
                            TempData["ErrorMessage"] = $"Insufficient stock for {medName}. Requested: {item.Quantity}, Available: {currentStock}. Dispensing cancelled.";
                            return RedirectToAction(nameof(Details), new { id });
                        }
                    }

                    // All items verified; apply mutations
                    existing.Status = PrescriptionStatus.Dispensed;
                    existing.DispensedById = pharmacistUserId;
                    existing.DispensedAt = DateTime.UtcNow;
                    existing.UpdatedAt = DateTime.UtcNow;

                    foreach (var item in items)
                    {
                        var medicine = await _context.Medicines.FindAsync(item.MedicineId);
                        if (medicine != null)
                        {
                            medicine.StockQuantity -= item.Quantity;
                            medicine.UpdatedAt = DateTime.UtcNow;
                        }
                    }

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();
                }

                TempData["SuccessMessage"] = "Medicines dispensed successfully. Stock has been updated.";
                return RedirectToAction(nameof(Details), new { id });
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                TempData["ErrorMessage"] = "An error occurred while dispensing medications.";
                return RedirectToAction(nameof(Details), new { id });
            }
        }
    }
}
