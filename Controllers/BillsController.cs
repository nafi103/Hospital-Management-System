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
    [Authorize(Roles = "Admin,Receptionist")]
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

            if (BillItems == null || BillItems.Count == 0)
            {
                ModelState.AddModelError("", "At least one billing line item is required.");
            }

            var subtotal = BillItems?.Sum(i => i.Amount) ?? 0;
            if (bill.DiscountAmount < 0)
            {
                ModelState.AddModelError(nameof(bill.DiscountAmount), "Discount amount cannot be negative.");
            }
            else if (bill.DiscountAmount > subtotal)
            {
                ModelState.AddModelError(nameof(bill.DiscountAmount), $"Discount amount (৳{bill.DiscountAmount:0.00}) cannot exceed the subtotal (৳{subtotal:0.00}).");
            }

            if (bill.DiscountAmount > 0 && !User.IsInRole("Admin"))
            {
                ModelState.AddModelError(nameof(bill.DiscountAmount), "Discounts require Administrator authorization and cannot be self-approved by receptionists.");
            }

            if (ModelState.IsValid)
            {
                await using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    bill.CreatedAt = DateTime.UtcNow;
                    bill.UpdatedAt = DateTime.UtcNow;
                    
                    if (BillItems != null)
                    {
                        bill.BillItems = BillItems;
                    }

                    if (bill.DiscountAmount > 0)
                    {
                        var userIdClaim = User.FindFirst("UserId")?.Value;
                        if (int.TryParse(userIdClaim, out int approverId))
                        {
                            bill.DiscountApprovedById = approverId;
                        }
                    }

                    bill.RecalculateTotals();
                    
                    _context.Add(bill);

                    // Extract only the specific prescription IDs that are actually billed in BillItems
                    var prescriptionIds = new List<int>();
                    if (BillItems != null)
                    {
                        foreach (var item in BillItems.Where(i => i.Department == DepartmentType.Pharmacy))
                        {
                            var match = System.Text.RegularExpressions.Regex.Match(item.Description ?? "", @"Prescription #(\d+)");
                            if (match.Success && int.TryParse(match.Groups[1].Value, out int presId))
                            {
                                prescriptionIds.Add(presId);
                            }
                        }
                    }

                    if (prescriptionIds.Count > 0)
                    {
                        var billedPrescriptions = await _context.Prescriptions
                            .Where(p => prescriptionIds.Contains(p.Id) && p.PatientId == bill.PatientId && !p.IsBilled)
                            .ToListAsync();

                        foreach (var pres in billedPrescriptions)
                        {
                            pres.IsBilled = true;
                            pres.UpdatedAt = DateTime.UtcNow;
                            _context.Update(pres);
                        }
                    }

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();
                    
                    TempData["SuccessMessage"] = "Bill generated successfully.";
                    return RedirectToAction(nameof(Details), new { id = bill.Id });
                }
                catch (Exception)
                {
                    await transaction.RollbackAsync();
                    ModelState.AddModelError("", "An error occurred while creating the bill. Changes have been rolled back.");
                }
            }

            if (bill.AdmissionId.HasValue)
            {
                var patient = await _context.Patients.FindAsync(bill.PatientId);
                if (patient != null)
                {
                    ViewBag.PatientName = patient.FullName;
                    ViewBag.PatientUhid = patient.Uhid;
                }
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
                .Include(b => b.PaymentTransactions)
                    .ThenInclude(pt => pt.ProcessedBy)
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
                .Include(b => b.PaymentTransactions)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (bill == null) return NotFound();

            var currentPaid = bill.PaymentTransactions.Sum(t => t.Amount);
            var balanceDue = bill.NetTotal - currentPaid;
            if (balanceDue <= 0)
            {
                TempData["SuccessMessage"] = "This invoice is already fully paid.";
                return RedirectToAction(nameof(Details), new { id });
            }

            return View(bill);
        }

        // POST: Bills/Payment/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Payment(int id, decimal PaymentAmount, string paymentMethod = "Cash", string? notes = null)
        {
            if (PaymentAmount <= 0)
            {
                TempData["ErrorMessage"] = "Payment amount must be greater than zero.";
                return RedirectToAction(nameof(Payment), new { id });
            }

            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var bill = await _context.Bills
                    .Include(b => b.BillItems)
                    .Include(b => b.PaymentTransactions)
                    .FirstOrDefaultAsync(m => m.Id == id);

                if (bill == null) return NotFound();

                var currentPaid = bill.PaymentTransactions.Sum(t => t.Amount);
                var balanceDue = bill.NetTotal - currentPaid;

                if (balanceDue <= 0)
                {
                    await transaction.RollbackAsync();
                    TempData["ErrorMessage"] = "This invoice has already been fully paid.";
                    return RedirectToAction(nameof(Details), new { id = bill.Id });
                }

                if (PaymentAmount > balanceDue)
                {
                    await transaction.RollbackAsync();
                    TempData["ErrorMessage"] = $"Payment amount (৳{PaymentAmount:0.00}) exceeds balance due (৳{balanceDue:0.00}).";
                    return RedirectToAction(nameof(Payment), new { id });
                }

                int? cashierId = null;
                var userIdClaim = User.Claims.FirstOrDefault(c => c.Type == "UserId")?.Value;
                if (int.TryParse(userIdClaim, out int uid))
                {
                    cashierId = uid;
                }

                var tx = new PaymentTransaction
                {
                    BillId = bill.Id,
                    Amount = PaymentAmount,
                    PaymentMethod = string.IsNullOrWhiteSpace(paymentMethod) ? "Cash" : paymentMethod.Trim(),
                    ProcessedById = cashierId,
                    TransactionDate = DateTime.UtcNow,
                    Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim()
                };

                _context.PaymentTransactions.Add(tx);

                // Derive bill.PaidAmount from all recorded ledger transactions
                bill.PaidAmount = currentPaid + PaymentAmount;
                bill.UpdatedAt = DateTime.UtcNow;
                bill.RecalculateTotals();

                _context.Update(bill);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                TempData["SuccessMessage"] = $"Payment of ৳{PaymentAmount:0.00} via {tx.PaymentMethod} recorded successfully.";
                return RedirectToAction(nameof(Details), new { id = bill.Id });
            }
            catch (DbUpdateConcurrencyException)
            {
                await transaction.RollbackAsync();
                TempData["ErrorMessage"] = "Another transaction or modification occurred concurrently on this bill. Please review the updated balance.";
                return RedirectToAction(nameof(Payment), new { id });
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                TempData["ErrorMessage"] = "An error occurred while recording the payment transaction.";
                return RedirectToAction(nameof(Payment), new { id });
            }
        }
    }
}
