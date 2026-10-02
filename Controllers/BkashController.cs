using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using HospitalManagementSystem.Models;
using HospitalManagementSystem.Services;

namespace HospitalManagementSystem.Controllers
{
    [Authorize(Roles = "Patient,Admin")]
    public class BkashController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IBkashPaymentService _bkashService;
        private readonly ILogger<BkashController> _logger;

        public BkashController(
            ApplicationDbContext context,
            IBkashPaymentService bkashService,
            ILogger<BkashController> logger)
        {
            _context = context;
            _bkashService = bkashService;
            _logger = logger;
        }

        private async Task<(Patient? PrimaryPatient, List<Patient> Dependents)> GetPatientContextAsync()
        {
            var userIdClaim = User.FindFirst("UserId")?.Value;
            var uhid = User.Identity?.Name;

            int.TryParse(userIdClaim, out int userId);

            var primaryPatient = await _context.Patients
                .FirstOrDefaultAsync(p => (userId > 0 && p.UserId == userId) || (!string.IsNullOrEmpty(uhid) && p.Uhid == uhid));

            if (primaryPatient == null)
            {
                return (null, new List<Patient>());
            }

            var dependents = await _context.Patients
                .Where(p => p.GuardianPatientId == primaryPatient.Id)
                .ToListAsync();

            return (primaryPatient, dependents);
        }

        // POST: Bkash/Pay?billId=5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Pay(int billId)
        {
            var bill = await _context.Bills
                .Include(b => b.Patient)
                .Include(b => b.PaymentTransactions)
                .FirstOrDefaultAsync(b => b.Id == billId);

            if (bill == null) return NotFound();

            // IDOR Protection: Verify ownership (self or linked dependent)
            if (!User.IsInRole("Admin"))
            {
                var (primaryPatient, dependents) = await GetPatientContextAsync();
                if (primaryPatient == null) return Forbid();

                var allowedIds = dependents.Select(d => d.Id).Append(primaryPatient.Id).ToHashSet();
                if (!allowedIds.Contains(bill.PatientId))
                {
                    _logger.LogWarning("IDOR attempt: User {User} attempted to pay Bill #{BillId} for unlinked Patient #{PatientId}",
                        User.Identity?.Name, bill.Id, bill.PatientId);
                    return Forbid();
                }
            }

            var currentPaid = bill.PaymentTransactions.Sum(t => t.Amount);
            var balanceDue = bill.NetTotal - currentPaid;

            if (balanceDue <= 0 || bill.Status == BillStatus.Paid)
            {
                TempData["ErrorMessage"] = "This invoice has already been fully paid.";
                return RedirectToAction("Bills", "Portal");
            }

            var callbackUrl = (Url != null && Request != null ? Url.Action(nameof(Callback), "Bkash", null, Request.Scheme) : null) 
                              ?? "/Bkash/Callback";

            var payerReference = bill.Patient?.Uhid ?? "HOSPITAL-PATIENT";

            _logger.LogInformation("Initiating bKash payment for Bill #{BillId}, amount ৳{Amount}", bill.Id, balanceDue);

            var createResponse = await _bkashService.CreatePaymentAsync(bill.Id, balanceDue, payerReference, callbackUrl);

            if (createResponse == null || string.IsNullOrEmpty(createResponse.BkashURL))
            {
                _logger.LogError("Failed to initiate bKash payment for Bill #{BillId}: {StatusMessage}",
                    bill.Id, createResponse?.StatusMessage);
                TempData["ErrorMessage"] = $"Unable to initiate bKash payment: {createResponse?.StatusMessage ?? "Connection error"}. Please try again.";
                return RedirectToAction("Bills", "Portal");
            }

            return Redirect(createResponse.BkashURL);
        }

        // GET: Bkash/Callback?paymentID=...&status=...
        [HttpGet]
        public async Task<IActionResult> Callback(string? paymentID, string? status)
        {
            _logger.LogInformation("bKash callback received: PaymentID={PaymentId}, Status={Status}", paymentID, status);

            if (string.Equals(status, "cancel", StringComparison.OrdinalIgnoreCase))
            {
                TempData["WarningMessage"] = "bKash checkout was cancelled. No charges were made to your account.";
                return RedirectToAction("Bills", "Portal");
            }

            if (string.Equals(status, "failure", StringComparison.OrdinalIgnoreCase))
            {
                TempData["ErrorMessage"] = "bKash payment failed or was declined by the provider. Please try again.";
                return RedirectToAction("Bills", "Portal");
            }

            if (string.IsNullOrEmpty(paymentID) || !string.Equals(status, "success", StringComparison.OrdinalIgnoreCase))
            {
                TempData["ErrorMessage"] = "Invalid payment callback response received from bKash.";
                return RedirectToAction("Bills", "Portal");
            }

            var executeResponse = await _bkashService.ExecutePaymentAsync(paymentID);

            if (executeResponse == null || executeResponse.StatusCode != "0000" ||
                !string.Equals(executeResponse.TransactionStatus, "Completed", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError("bKash execution failed for PaymentID {PaymentId}: Status={Status}, Message={Message}",
                    paymentID, executeResponse?.TransactionStatus, executeResponse?.StatusMessage);
                TempData["ErrorMessage"] = $"bKash payment execution was not successful: {executeResponse?.StatusMessage ?? "Provider error"}.";
                return RedirectToAction("Bills", "Portal");
            }

            // Extract Bill ID from MerchantInvoiceNumber ("BILL-{billId}")
            int billId = 0;
            if (!string.IsNullOrEmpty(executeResponse.MerchantInvoiceNumber) &&
                executeResponse.MerchantInvoiceNumber.StartsWith("BILL-", StringComparison.OrdinalIgnoreCase))
            {
                int.TryParse(executeResponse.MerchantInvoiceNumber.Substring(5), out billId);
            }

            if (billId <= 0)
            {
                _logger.LogError("Failed to parse BillId from bKash MerchantInvoiceNumber: {Invoice}",
                    executeResponse.MerchantInvoiceNumber);
                TempData["ErrorMessage"] = "Unable to correlate payment with hospital invoice.";
                return RedirectToAction("Bills", "Portal");
            }

            var bill = await _context.Bills
                .Include(b => b.PaymentTransactions)
                .FirstOrDefaultAsync(b => b.Id == billId);

            if (bill == null)
            {
                _logger.LogError("Bill #{BillId} not found during bKash callback", billId);
                TempData["ErrorMessage"] = "The invoice associated with this payment was not found.";
                return RedirectToAction("Bills", "Portal");
            }

            // IDOR Protection: Verify caller is permitted to record/view this bill
            if (!User.IsInRole("Admin"))
            {
                var (primaryPatient, dependents) = await GetPatientContextAsync();
                if (primaryPatient == null) return Forbid();

                var allowedIds = dependents.Select(d => d.Id).Append(primaryPatient.Id).ToHashSet();
                if (!allowedIds.Contains(bill.PatientId))
                {
                    _logger.LogWarning("IDOR attempt in callback: User {User} attempted to finalize Bill #{BillId}",
                        User.Identity?.Name, bill.Id);
                    return Forbid();
                }
            }

            var trxId = executeResponse.TrxID ?? paymentID;

            // Atomic Transaction with Concurrency & Idempotency Enforcement
            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // 1. Idempotency Check: prevent duplicate recording of the same transaction
                var existingTx = await _context.PaymentTransactions
                    .FirstOrDefaultAsync(t => t.TransactionId == trxId);

                if (existingTx != null)
                {
                    await transaction.RollbackAsync();
                    _logger.LogInformation("Idempotent callback ignored: TrxID {TrxId} already recorded on Bill #{BillId}", trxId, bill.Id);
                    TempData["SuccessMessage"] = $"Payment of ৳{existingTx.Amount:0.00} via bKash was already recorded. TrxID: {trxId}";
                    return RedirectToAction("Bills", "Portal");
                }

                if (!decimal.TryParse(executeResponse.Amount, NumberStyles.Number, CultureInfo.InvariantCulture, out var paidAmount) || paidAmount <= 0)
                {
                    await transaction.RollbackAsync();
                    TempData["ErrorMessage"] = "Invalid payment amount received from bKash.";
                    return RedirectToAction("Bills", "Portal");
                }

                var currentPaid = bill.PaymentTransactions.Sum(t => t.Amount);

                var paymentTx = new PaymentTransaction
                {
                    BillId = bill.Id,
                    Amount = paidAmount,
                    PaymentMethod = "bKash",
                    TransactionId = trxId,
                    TransactionDate = DateTime.UtcNow,
                    Notes = $"bKash Tokenized Checkout | PaymentID: {executeResponse.PaymentID} | TrxID: {trxId} | Payer: {executeResponse.CustomerMsisdn ?? executeResponse.PayerReference ?? "N/A"}"
                };

                _context.PaymentTransactions.Add(paymentTx);

                bill.PaidAmount = currentPaid + paidAmount;
                bill.UpdatedAt = DateTime.UtcNow;
                bill.RecalculateTotals();

                _context.Update(bill);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                _logger.LogInformation("bKash payment recorded successfully: Bill #{BillId}, TrxID {TrxId}, Amount ৳{Amount}",
                    bill.Id, trxId, paidAmount);

                TempData["SuccessMessage"] = $"Payment of ৳{paidAmount:0.00} via bKash completed successfully! Transaction ID: {trxId}";
                return RedirectToAction("Bills", "Portal");
            }
            catch (DbUpdateConcurrencyException)
            {
                await transaction.RollbackAsync();
                _logger.LogWarning("Concurrency conflict occurred when updating Bill #{BillId} via bKash callback", bill.Id);
                TempData["ErrorMessage"] = "Another transaction occurred concurrently on this bill. Please review your updated balance.";
                return RedirectToAction("Bills", "Portal");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Unexpected error recording bKash transaction {TrxId} for Bill #{BillId}", trxId, bill.Id);
                TempData["ErrorMessage"] = $"An error occurred while recording the payment in the hospital ledger. Please contact support with TrxID: {trxId}";
                return RedirectToAction("Bills", "Portal");
            }
        }
    }
}
