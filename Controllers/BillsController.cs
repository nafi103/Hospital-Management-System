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
    [Authorize(Roles = "Admin")]
    public class BillsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public BillsController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: Bills
        public async Task<IActionResult> Index()
        {
            var bills = await _context.Bills
                .Include(b => b.Patient)
                .OrderByDescending(b => b.CreatedAt)
                .ToListAsync();
            return View(bills);
        }

        // GET: Bills/Create
        public async Task<IActionResult> Create(int? admissionId)
        {
            var bill = new Bill();
            
            if (admissionId.HasValue)
            {
                var admission = await _context.Admissions
                    .Include(a => a.Patient)
                    .Include(a => a.BedTransfers).ThenInclude(bt => bt.Bed)
                    .FirstOrDefaultAsync(a => a.Id == admissionId.Value);

                if (admission != null)
                {
                    bill.PatientId = admission.PatientId;
                    bill.AdmissionId = admission.Id;
                    ViewBag.PatientName = admission.Patient.FullName;
                    ViewBag.PatientUhid = admission.Patient.Uhid;

                    // Auto-calculate bed charges
                    foreach (var transfer in admission.BedTransfers)
                    {
                        var end = transfer.EndDate ?? DateTime.UtcNow;
                        var duration = end - transfer.StartDate;
                        var days = Math.Max(1, (int)Math.Ceiling(duration.TotalDays));
                        var amount = days * transfer.Bed.DailyRate;

                        bill.BillItems.Add(new BillItem
                        {
                            Department = DepartmentType.CabinRent,
                            Description = $"Bed {transfer.Bed.BedNumber} ({transfer.Bed.Category}) - {days} Days",
                            Amount = amount
                        });
                    }

                    // Auto-calculate pharmacy charges (unbilled dispensed prescriptions for this patient)
                    var unbilledPrescriptions = await _context.Prescriptions
                        .Include(p => p.PrescriptionItems).ThenInclude(pi => pi.Medicine)
                        .Where(p => p.PatientId == admission.PatientId && p.Status == PrescriptionStatus.Dispensed && !p.IsBilled)
                        .ToListAsync();

                    foreach (var pres in unbilledPrescriptions)
                    {
                        decimal presTotal = pres.PrescriptionItems.Sum(pi => pi.Quantity * pi.UnitPrice);
                        bill.BillItems.Add(new BillItem
                        {
                            Department = DepartmentType.Pharmacy,
                            Description = $"Prescription #{pres.Id} - {pres.CreatedAt.ToString("MMM dd, yyyy")}",
                            Amount = presTotal
                        });
                    }
                }
            }

            return View(bill);
        }

        // POST: Bills/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("PatientId,AdmissionId,DiscountAmount")] Bill bill, List<BillItem> BillItems)
        {
            ModelState.Remove("Patient");
            ModelState.Remove("Admission");
            ModelState.Remove("DiscountApprovedBy");
            ModelState.Remove("BillItems");

            if (ModelState.IsValid)
            {
                bill.CreatedAt = DateTime.UtcNow;
                bill.UpdatedAt = DateTime.UtcNow;
                
                if (BillItems != null)
                {
                    bill.BillItems = BillItems;
                }

                // If discount > 0, we could require approval, but for now we'll auto-approve as Admin (Id=1)
                if (bill.DiscountAmount > 0)
                {
                    bill.DiscountApprovedById = 1; // Super Admin mock
                }

                bill.RecalculateTotals();
                
                _context.Add(bill);

                // Mark pulled prescriptions as billed
                // Wait, we need to know which prescriptions were pulled. We can find them again.
                if (bill.AdmissionId.HasValue)
                {
                    var unbilledPrescriptions = await _context.Prescriptions
                        .Where(p => p.PatientId == bill.PatientId && p.Status == PrescriptionStatus.Dispensed && !p.IsBilled)
                        .ToListAsync();

                    foreach (var pres in unbilledPrescriptions)
                    {
                        pres.IsBilled = true;
                        _context.Update(pres);
                    }
                }

                await _context.SaveChangesAsync();
                
                TempData["SuccessMessage"] = "Bill generated successfully.";
                return RedirectToAction(nameof(Details), new { id = bill.Id });
            }

            return View(bill);
        }

        // GET: Bills/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var bill = await _context.Bills
                .Include(b => b.Patient)
                .Include(b => b.Admission)
                .Include(b => b.BillItems)
                .Include(b => b.DiscountApprovedBy)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (bill == null) return NotFound();

            return View(bill);
        }

        // GET: Bills/Payment/5
        public async Task<IActionResult> Payment(int? id)
        {
            if (id == null) return NotFound();

            var bill = await _context.Bills
                .Include(b => b.Patient)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (bill == null) return NotFound();

            return View(bill);
        }

        // POST: Bills/Payment/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Payment(int id, decimal PaymentAmount)
        {
            var bill = await _context.Bills
                .Include(b => b.BillItems)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (bill == null) return NotFound();

            if (PaymentAmount > 0)
            {
                bill.PaidAmount += PaymentAmount;
                bill.UpdatedAt = DateTime.UtcNow;
                bill.RecalculateTotals();
                
                _context.Update(bill);
                await _context.SaveChangesAsync();
                
                TempData["SuccessMessage"] = $"Payment of ৳{PaymentAmount:0.00} recorded successfully.";
            }

            return RedirectToAction(nameof(Details), new { id = bill.Id });
        }
    }
}
