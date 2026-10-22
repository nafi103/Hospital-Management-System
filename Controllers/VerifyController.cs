using System;
using System.Linq;
using System.Threading.Tasks;
using HospitalManagementSystem.Models;
using HospitalManagementSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HospitalManagementSystem.Controllers
{
    public class PrescriptionVerificationViewModel
    {
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
        public int PrescriptionId { get; set; }
        public string Uhid { get; set; } = string.Empty;
        public DateTime IssuedAt { get; set; }
        public string Signature { get; set; } = string.Empty;
        public Prescription? Prescription { get; set; }
        public bool IsPharmacistOrDoctor { get; set; }
    }

    [AllowAnonymous]
    public class VerifyController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IPrescriptionSecurityService _securityService;

        public VerifyController(ApplicationDbContext context, IPrescriptionSecurityService securityService)
        {
            _context = context;
            _securityService = securityService;
        }

        [HttpGet]
        public async Task<IActionResult> Prescription(int id, string? uhid, long? ts, string? sig)
        {
            var vm = new PrescriptionVerificationViewModel
            {
                PrescriptionId = id,
                Uhid = uhid ?? "",
                Signature = sig ?? "",
                IsPharmacistOrDoctor = User?.Identity?.IsAuthenticated == true && (User.IsInRole("Pharmacist") || User.IsInRole("Doctor") || User.IsInRole("Admin"))
            };

            if (id <= 0 || string.IsNullOrWhiteSpace(uhid) || ts == null || string.IsNullOrWhiteSpace(sig))
            {
                vm.IsValid = false;
                vm.ErrorMessage = "Incomplete verification parameters. The QR code or link is invalid.";
                return View(vm);
            }

            var issuedAt = new DateTime(ts.Value, DateTimeKind.Utc);
            vm.IssuedAt = issuedAt;

            // 1. Cryptographic HMAC-SHA256 signature verification
            bool isSignatureValid = _securityService.VerifySignature(id, uhid, issuedAt, sig);
            if (!isSignatureValid)
            {
                vm.IsValid = false;
                vm.ErrorMessage = "CRYPTOGRAPHIC INTEGRITY FAILURE: Digital HMAC-SHA256 signature does not match. This prescription document may be forged, modified, or tampered with.";
                return View(vm);
            }

            // 2. Database record verification
            var prescription = await _context.Prescriptions
                .Include(p => p.Patient)
                .Include(p => p.Doctor)
                .Include(p => p.PrescriptionItems)
                    .ThenInclude(pi => pi.Medicine)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (prescription == null)
            {
                vm.IsValid = false;
                vm.ErrorMessage = $"Prescription #{id} does not exist in the hospital database.";
                return View(vm);
            }

            if (!string.Equals(prescription.Patient?.Uhid?.Trim(), uhid.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                vm.IsValid = false;
                vm.ErrorMessage = $"UHID Mismatch: Token claims UHID '{uhid}', but database record belongs to '{prescription.Patient?.Uhid}'.";
                return View(vm);
            }

            vm.IsValid = true;
            vm.Prescription = prescription;
            return View(vm);
        }
    }
}
