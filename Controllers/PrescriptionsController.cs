using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using HospitalManagementSystem.Models;

namespace HospitalManagementSystem.Controllers
{
    [Authorize(Roles = "Doctor")]
    public class PrescriptionsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public PrescriptionsController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: Prescriptions
        public async Task<IActionResult> Index()
        {
            var prescriptions = await _context.Prescriptions
                .Include(p => p.Patient)
                .Include(p => p.Doctor)
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync();
            return View(prescriptions);
        }

        // GET: Prescriptions/Create
        public IActionResult Create(int? patientId, int? doctorId)
        {
            var doctors = _context.Users
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
            
            // Available medicines for dropdown
            var medicines = _context.Medicines
                .Where(m => m.StockQuantity > 0)
                .Select(m => new { 
                    m.Id, 
                    DisplayName = m.Name + " (৳" + m.UnitPrice.ToString("0.00") + ")",
                    m.UnitPrice,
                    m.StockQuantity
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

        // POST: Prescriptions/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("PatientId,DoctorId,Notes")] Prescription prescription, List<PrescriptionItem> PrescriptionItems)
        {
            ModelState.Remove("Patient");
            ModelState.Remove("Doctor");
            ModelState.Remove("PrescriptionItems");

            if (ModelState.IsValid && PrescriptionItems != null && PrescriptionItems.Count > 0)
            {
                prescription.Status = PrescriptionStatus.PendingPharmacy;
                prescription.CreatedAt = DateTime.UtcNow;
                prescription.UpdatedAt = DateTime.UtcNow;

                // Validate and add items
                foreach(var item in PrescriptionItems)
                {
                    var medicine = await _context.Medicines.FindAsync(item.MedicineId);
                    if(medicine != null)
                    {
                        item.UnitPrice = medicine.UnitPrice; // Lock in the price at time of prescribing
                        prescription.PrescriptionItems.Add(item);
                    }
                }

                _context.Add(prescription);
                await _context.SaveChangesAsync();
                
                TempData["SuccessMessage"] = "Prescription created and sent to pharmacy.";
                return RedirectToAction(nameof(Index));
            }

            if (PrescriptionItems == null || PrescriptionItems.Count == 0)
            {
                ModelState.AddModelError("", "You must add at least one medicine to the prescription.");
            }

            var doctors = _context.Users.Where(u => u.Role.RoleName == "Doctor").Select(u => new { u.Id, u.FullName }).ToList();
            ViewData["DoctorId"] = new SelectList(doctors, "Id", "FullName", prescription.DoctorId);
            
            var medicines = _context.Medicines.Where(m => m.StockQuantity > 0).Select(m => new { m.Id, DisplayName = m.Name + " (৳" + m.UnitPrice.ToString("0.00") + ")", m.UnitPrice, m.StockQuantity }).ToList();
            ViewBag.MedicinesList = medicines;

            return View(prescription);
        }

        // GET: Prescriptions/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var prescription = await _context.Prescriptions
                .Include(p => p.Patient)
                .Include(p => p.Doctor)
                .Include(p => p.PrescriptionItems)
                    .ThenInclude(pi => pi.Medicine)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (prescription == null) return NotFound();

            return View(prescription);
        }

        // POST: Prescriptions/Dispense/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Dispense(int id)
        {
            var prescription = await _context.Prescriptions
                .Include(p => p.PrescriptionItems)
                    .ThenInclude(pi => pi.Medicine)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (prescription == null) return NotFound();

            if (prescription.Status == PrescriptionStatus.Dispensed)
            {
                TempData["ErrorMessage"] = "This prescription has already been dispensed.";
                return RedirectToAction(nameof(Details), new { id = prescription.Id });
            }

            // Check stock first
            foreach(var item in prescription.PrescriptionItems)
            {
                if (item.Medicine.StockQuantity < item.Quantity)
                {
                    TempData["ErrorMessage"] = $"Insufficient stock for {item.Medicine.Name}. Requested: {item.Quantity}, Available: {item.Medicine.StockQuantity}.";
                    return RedirectToAction(nameof(Details), new { id = prescription.Id });
                }
            }

            // Deduct stock
            foreach(var item in prescription.PrescriptionItems)
            {
                item.Medicine.StockQuantity -= item.Quantity;
                _context.Update(item.Medicine);
            }

            prescription.Status = PrescriptionStatus.Dispensed;
            prescription.UpdatedAt = DateTime.UtcNow;
            _context.Update(prescription);

            await _context.SaveChangesAsync();
            
            TempData["SuccessMessage"] = "Medicines dispensed successfully. Stock has been updated.";
            return RedirectToAction(nameof(Details), new { id = prescription.Id });
        }
    }
}
