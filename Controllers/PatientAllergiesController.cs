using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using HospitalManagementSystem.Models;

namespace HospitalManagementSystem.Controllers
{
    [Authorize]
    public class PatientAllergiesController : Controller
    {
        private readonly ApplicationDbContext _context;

        public PatientAllergiesController(ApplicationDbContext context)
        {
            _context = context;
        }

        // POST: PatientAllergies/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(int patientId, string substance, string? reactionType, AllergySeverity severity, string? allergenGenericName)
        {
            var patient = await _context.Patients.FindAsync(patientId);
            if (patient == null) return NotFound();

            if (string.IsNullOrWhiteSpace(substance))
            {
                TempData["ErrorMessage"] = "Substance is required to record an allergy.";
                return RedirectToAction("Details", "Patients", new { id = patientId });
            }

            var userIdClaim = User.Claims.FirstOrDefault(c => c.Type == "UserId")?.Value;
            if (userIdClaim == null || !int.TryParse(userIdClaim, out int recordedById))
            {
                return Forbid();
            }

            var allergy = new PatientAllergy
            {
                PatientId = patientId,
                Substance = substance.Trim(),
                AllergenGenericName = string.IsNullOrWhiteSpace(allergenGenericName) ? null : allergenGenericName.Trim(),
                ReactionType = string.IsNullOrWhiteSpace(reactionType) ? null : reactionType.Trim(),
                Severity = severity,
                RecordedById = recordedById,
                CreatedAt = DateTime.UtcNow
            };

            _context.PatientAllergies.Add(allergy);

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                TempData["ErrorMessage"] = $"{allergy.Substance} is already recorded as an allergy for this patient.";
                return RedirectToAction("Details", "Patients", new { id = patientId });
            }

            TempData["SuccessMessage"] = $"Allergy to {allergy.Substance} recorded.";
            return RedirectToAction("Details", "Patients", new { id = patientId });
        }

        // POST: PatientAllergies/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var allergy = await _context.PatientAllergies.FindAsync(id);
            if (allergy == null) return NotFound();

            int patientId = allergy.PatientId;
            _context.PatientAllergies.Remove(allergy);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Allergy record removed.";
            return RedirectToAction("Details", "Patients", new { id = patientId });
        }
    }
}
