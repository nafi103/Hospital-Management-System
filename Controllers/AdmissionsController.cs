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
    [Authorize(Roles = "Admin")]
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

            if (ModelState.IsValid)
            {
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
                
                TempData["SuccessMessage"] = "Patient admitted successfully.";
                return RedirectToAction(nameof(Index));
            }

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
                .FirstOrDefaultAsync(a => a.Id == id && a.DischargeDate == null);
                
            if (admission == null) return NotFound();

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
            var admission = await _context.Admissions
                .Include(a => a.BedTransfers)
                .FirstOrDefaultAsync(a => a.Id == id && a.DischargeDate == null);

            if (admission == null) return NotFound();

            var activeTransfer = admission.BedTransfers.FirstOrDefault(bt => bt.EndDate == null);
            if (activeTransfer != null)
            {
                if (activeTransfer.BedId == BedId)
                {
                    TempData["SuccessMessage"] = "Patient is already in this bed.";
                    return RedirectToAction(nameof(Index));
                }
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
            TempData["SuccessMessage"] = "Patient successfully transferred to the new bed.";
            
            return RedirectToAction(nameof(Index));
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

            return View(admission);
        }

        // POST: Admissions/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var admission = await _context.Admissions
                .Include(a => a.BedTransfers)
                .FirstOrDefaultAsync(a => a.Id == id);
            
            if (admission != null)
            {
                _context.Admissions.Remove(admission);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Admission deleted successfully.";
            }

            return RedirectToAction(nameof(Index));
        }

        private bool AdmissionExists(int id)
        {
            return _context.Admissions.Any(e => e.Id == id);
        }
    }
}
