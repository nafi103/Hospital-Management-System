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
    [Authorize(Roles = "Admin,Receptionist")]
    public class AdmissionsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AdmissionsController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: Admissions
        public async Task<IActionResult> Index()
        {
            var admissions = await _context.Admissions
                .Include(a => a.Patient)
                .Include(a => a.AdmittingDoctor)
                .Include(a => a.BedTransfers).ThenInclude(bt => bt.Bed)
                .OrderByDescending(a => a.AdmissionDate)
                .ToListAsync();
            return View(admissions);
        }

        // GET: Admissions/Create
        public IActionResult Create()
        {
            // Patient will be populated via AJAX, so we don't load them here
            
            ViewData["DoctorId"] = new SelectList(_context.Users.Where(u => u.Role.RoleName == "Doctor"), "Id", "FullName");

            // Only show available beds
            var availableBeds = _context.Beds
                .Include(b => b.BedTransfers)
                .ToList() // Client side evaluation for Status property
                .Where(b => b.Status == "Available")
                .Select(b => new {
                    Id = b.Id,
                    Category = b.Category.ToString(),
                    DisplayName = b.BedNumber + " (" + b.Category + ")"
                }).ToList();

            ViewBag.AvailableBedsList = availableBeds;

            return View();
        }

        // POST: Admissions/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("PatientId,AdmittingDoctorId,AdmissionDate")] Admission admission, int? BedId)
        {
            ModelState.Remove("Patient");
            ModelState.Remove("AdmittingDoctor");
            ModelState.Remove("BedTransfers");

            var hasActiveAdmission = await _context.Admissions.AnyAsync(a => a.PatientId == admission.PatientId && a.DischargeDate == null);
            if (hasActiveAdmission)
            {
                ModelState.AddModelError("", "This patient already has an active inpatient admission. A patient cannot have multiple simultaneous admissions.");
            }

            if (BedId.HasValue)
            {
                var isOccupied = await _context.BedTransfers.AnyAsync(bt => bt.BedId == BedId.Value && bt.EndDate == null);
                if (isOccupied)
                {
                    ModelState.AddModelError("", "The selected bed is currently occupied. Please choose an available bed.");
                }
            }

            if (ModelState.IsValid)
            {
                await using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    var hasActiveAdmissionConcurrently = await _context.Admissions.AnyAsync(a => a.PatientId == admission.PatientId && a.DischargeDate == null);
                    if (hasActiveAdmissionConcurrently)
                    {
                        await transaction.RollbackAsync();
                        ModelState.AddModelError("", "This patient already has an active inpatient admission.");
                        goto Repopulate;
                    }

                    if (BedId.HasValue)
                    {
                        var isOccupied = await _context.BedTransfers.AnyAsync(bt => bt.BedId == BedId.Value && bt.EndDate == null);
                        if (isOccupied)
                        {
                            await transaction.RollbackAsync();
                            ModelState.AddModelError("", "The selected bed is already occupied. Please choose an available bed.");
                            goto Repopulate;
                        }
                    }

                    admission.AdmissionDate = DateTime.SpecifyKind(admission.AdmissionDate, DateTimeKind.Utc);
                    admission.CreatedAt = DateTime.UtcNow;
                    admission.UpdatedAt = DateTime.UtcNow;

                    if (BedId.HasValue)
                    {
                        // Add Bed Transfer record
                        var transfer = new BedTransfer
                        {
                            BedId = BedId.Value,
                            StartDate = admission.AdmissionDate
                        };
                        admission.BedTransfers.Add(transfer);
                    }

                    _context.Add(admission);
                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();
                    
                    TempData["SuccessMessage"] = "Patient admitted successfully.";
                    return RedirectToAction(nameof(Index));
                }
                catch (Exception)
                {
                    await transaction.RollbackAsync();
                    ModelState.AddModelError("", "An unexpected error occurred while creating the admission.");
                }
            }

        Repopulate:

            // Repopulate ViewDatas on error
            
            ViewData["DoctorId"] = new SelectList(_context.Users.Where(u => u.Role.RoleName == "Doctor"), "Id", "FullName", admission.AdmittingDoctorId);

            var availableBeds = _context.Beds
                .Include(b => b.BedTransfers)
                .ToList()
                .Where(b => b.Status == "Available" || (BedId.HasValue && b.Id == BedId.Value))
                .Select(b => new {
                    Id = b.Id,
                    Category = b.Category.ToString(),
                    DisplayName = b.BedNumber + " (" + b.Category + ")"
                }).ToList();

            ViewBag.AvailableBedsList = availableBeds;
            ViewBag.SelectedBedId = BedId;

            return View(admission);
        }
        
        // GET: Admissions/Discharge/5
        public async Task<IActionResult> Discharge(int? id)
        {
            if (id == null) return NotFound();

            var admission = await _context.Admissions
                .Include(a => a.Patient)
                .Include(a => a.BedTransfers).ThenInclude(bt => bt.Bed)
                .FirstOrDefaultAsync(m => m.Id == id);
                
            if (admission == null) return NotFound();

            if (admission.DischargeDate.HasValue)
            {
                TempData["ErrorMessage"] = "This patient has already been discharged.";
                return RedirectToAction(nameof(Index));
            }

            // Check unbilled dispensed prescriptions
            var unbilledPrescriptions = await _context.Prescriptions
                .Include(p => p.PrescriptionItems).ThenInclude(pi => pi.Medicine)
                .Where(p => p.PatientId == admission.PatientId && p.Status == PrescriptionStatus.Dispensed && !p.IsBilled)
                .ToListAsync();
            decimal unbilledPharmacyTotal = unbilledPrescriptions.Sum(p => p.PrescriptionItems.Sum(pi => pi.Quantity * pi.UnitPrice));

            // Check accrued vs billed cabin rent
            decimal accruedCabinRent = 0m;
            int totalStayDays = 0;
            foreach (var transfer in admission.BedTransfers)
            {
                var end = transfer.EndDate ?? DateTime.UtcNow;
                var duration = end - transfer.StartDate;
                var days = Math.Max(1, (int)Math.Ceiling(duration.TotalDays));
                totalStayDays += days;
                accruedCabinRent += days * (transfer.Bed?.DailyRate ?? 0m);
            }

            var existingBills = await _context.Bills
                .Include(b => b.BillItems)
                .Where(b => b.AdmissionId == admission.Id)
                .ToListAsync();

            decimal billedCabinRent = existingBills
                .SelectMany(b => b.BillItems)
                .Where(bi => bi.Department == DepartmentType.CabinRent)
                .Sum(bi => bi.Amount);

            decimal unbilledCabinRent = Math.Max(0m, accruedCabinRent - billedCabinRent);

            // Check outstanding unpaid bills for this admission/patient
            var unpaidBills = await _context.Bills
                .Where(b => (b.AdmissionId == admission.Id || b.PatientId == admission.PatientId) && b.Status != BillStatus.Paid)
                .ToListAsync();
            decimal outstandingBalance = unpaidBills.Sum(b => b.NetTotal - b.PaidAmount);

            ViewBag.UnbilledPrescriptionsCount = unbilledPrescriptions.Count;
            ViewBag.UnbilledPharmacyTotal = unbilledPharmacyTotal;
            ViewBag.TotalStayDays = totalStayDays;
            ViewBag.UnbilledCabinRent = unbilledCabinRent;
            ViewBag.AccruedCabinRent = accruedCabinRent;
            ViewBag.OutstandingBillsCount = unpaidBills.Count;
            ViewBag.OutstandingBalance = outstandingBalance;
            ViewBag.HasPendingFinancials = (unbilledPrescriptions.Count > 0 || unbilledCabinRent > 0 || outstandingBalance > 0);

            return View(admission);
        }

        // POST: Admissions/Discharge/5
        [HttpPost, ActionName("Discharge")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DischargeConfirmed(int id)
        {
            var admission = await _context.Admissions
                .Include(a => a.BedTransfers)
                .FirstOrDefaultAsync(a => a.Id == id);
                
            if (admission == null) return NotFound();

            if (admission.DischargeDate.HasValue)
            {
                TempData["ErrorMessage"] = "This patient has already been discharged.";
                return RedirectToAction(nameof(Index));
            }

            // Set Discharge date
            admission.DischargeDate = DateTime.UtcNow;
            admission.UpdatedAt = DateTime.UtcNow;
            
            // Release active bed
            var activeTransfer = admission.BedTransfers.FirstOrDefault(bt => bt.EndDate == null);
            if (activeTransfer != null)
            {
                activeTransfer.EndDate = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Patient discharged successfully.";
            return RedirectToAction(nameof(Index));
        }

        // GET: Admissions/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var admission = await _context.Admissions
                .Include(a => a.Patient)
                .FirstOrDefaultAsync(a => a.Id == id);
                
            if (admission == null) return NotFound();

            ViewData["DoctorId"] = new SelectList(_context.Users.Where(u => u.Role.RoleName == "Doctor"), "Id", "FullName", admission.AdmittingDoctorId);
            return View(admission);
        }

        // POST: Admissions/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,PatientId,AdmittingDoctorId,AdmissionDate,DischargeDate,CreatedAt")] Admission admission)
        {
            if (id != admission.Id) return NotFound();

            ModelState.Remove("Patient");
            ModelState.Remove("AdmittingDoctor");
            ModelState.Remove("BedTransfers");

            if (ModelState.IsValid)
            {
                try
                {
                    admission.AdmissionDate = DateTime.SpecifyKind(admission.AdmissionDate, DateTimeKind.Utc);
                    if (admission.DischargeDate.HasValue)
                    {
                        admission.DischargeDate = DateTime.SpecifyKind(admission.DischargeDate.Value, DateTimeKind.Utc);
                    }
                    admission.UpdatedAt = DateTime.UtcNow;

                    _context.Update(admission);
                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = "Admission updated successfully.";
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!AdmissionExists(admission.Id)) return NotFound();
                    else throw;
                }
                return RedirectToAction(nameof(Index));
            }
            ViewData["DoctorId"] = new SelectList(_context.Users.Where(u => u.Role.RoleName == "Doctor"), "Id", "FullName", admission.AdmittingDoctorId);
            
            // Load Patient for the view since it's removed from ModelState
            admission.Patient = await _context.Patients.FindAsync(admission.PatientId);
            
            return View(admission);
        }

        // GET: Admissions/TransferBed/5
        public async Task<IActionResult> TransferBed(int? id)
        {
            if (id == null) return NotFound();

            var admission = await _context.Admissions
                .Include(a => a.Patient)
                .Include(a => a.BedTransfers).ThenInclude(bt => bt.Bed)
                .FirstOrDefaultAsync(a => a.Id == id);
                
            if (admission == null) return NotFound();

            if (admission.DischargeDate.HasValue)
            {
                TempData["ErrorMessage"] = "Cannot transfer beds for a patient who has already been discharged.";
                return RedirectToAction(nameof(Index));
            }

            var currentTransfer = admission.BedTransfers.FirstOrDefault(bt => bt.EndDate == null);
            ViewBag.CurrentBed = currentTransfer?.Bed;

            var availableBeds = _context.Beds
                .Include(b => b.BedTransfers)
                .ToList()
                .Where(b => b.Status == "Available")
                .Select(b => new {
                    Id = b.Id,
                    Category = b.Category.ToString(),
                    DisplayName = b.BedNumber + " (" + b.Category + ")"
                }).ToList();

            ViewBag.AvailableBedsList = availableBeds;
            return View(admission);
        }

        // POST: Admissions/TransferBed/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TransferBed(int id, int BedId)
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var admission = await _context.Admissions
                    .Include(a => a.BedTransfers)
                    .FirstOrDefaultAsync(a => a.Id == id);

                if (admission == null) return NotFound();

                if (admission.DischargeDate.HasValue)
                {
                    await transaction.RollbackAsync();
                    TempData["ErrorMessage"] = "Cannot transfer beds for a patient who has already been discharged.";
                    return RedirectToAction(nameof(Index));
                }

                var activeTransfer = admission.BedTransfers.FirstOrDefault(bt => bt.EndDate == null);
                if (activeTransfer != null && activeTransfer.BedId == BedId)
                {
                    TempData["SuccessMessage"] = "Patient is already in this bed.";
                    return RedirectToAction(nameof(Index));
                }

                // Server-side transactional validation ensuring the target bed is not occupied
                var isOccupied = await _context.BedTransfers.AnyAsync(bt => bt.BedId == BedId && bt.EndDate == null);
                if (isOccupied)
                {
                    await transaction.RollbackAsync();
                    TempData["ErrorMessage"] = "The target bed is currently occupied. Please choose an available bed.";
                    return RedirectToAction(nameof(TransferBed), new { id });
                }

                if (activeTransfer != null)
                {
                    activeTransfer.EndDate = DateTime.UtcNow;
                }

                var newTransfer = new BedTransfer
                {
                    BedId = BedId,
                    StartDate = DateTime.UtcNow
                };
                
                admission.BedTransfers.Add(newTransfer);
                admission.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                TempData["SuccessMessage"] = "Patient successfully transferred to the new bed.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                TempData["ErrorMessage"] = "An unexpected error occurred while transferring beds.";
                return RedirectToAction(nameof(TransferBed), new { id });
            }
        }

        // GET: Admissions/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();

            var admission = await _context.Admissions
                .Include(a => a.Patient)
                .Include(a => a.AdmittingDoctor)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (admission == null) return NotFound();

            ViewBag.HasBills = await _context.Bills.AnyAsync(b => b.AdmissionId == id);

            return View(admission);
        }

        // POST: Admissions/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            if (await _context.Bills.AnyAsync(b => b.AdmissionId == id))
            {
                TempData["ErrorMessage"] = "Cannot delete this admission because billing records are attached to it. Please void or adjust associated bills first.";
                return RedirectToAction(nameof(Index));
            }

            var admission = await _context.Admissions
                .Include(a => a.BedTransfers)
                .FirstOrDefaultAsync(a => a.Id == id);
            
            if (admission != null)
            {
                try
                {
                    _context.Admissions.Remove(admission);
                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = "Admission deleted successfully.";
                }
                catch (DbUpdateException)
                {
                    TempData["ErrorMessage"] = "Unable to delete admission due to linked database records. Ensure all related bills and transactions are resolved.";
                }
            }

            return RedirectToAction(nameof(Index));
        }

        private bool AdmissionExists(int id)
        {
            return _context.Admissions.Any(e => e.Id == id);
        }
    }
}
