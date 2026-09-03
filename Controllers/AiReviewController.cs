using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HospitalManagementSystem.Models;
using HospitalManagementSystem.Services;

namespace HospitalManagementSystem.Controllers
{
    [Authorize]
    public class AiReviewController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IClinicalAiService _aiService;

        public AiReviewController(ApplicationDbContext context, IClinicalAiService aiService)
        {
            _context = context;
            _aiService = aiService;
        }

        private int? CurrentUserId()
        {
            var claim = User.Claims.FirstOrDefault(c => c.Type == "UserId")?.Value;
            return int.TryParse(claim, out int id) ? id : null;
        }

        // POST: AiReview/GenerateCaseNoteDraft
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GenerateCaseNoteDraft(int patientId)
        {
            var userId = CurrentUserId();
            if (userId == null) return Forbid();

            try
            {
                await _aiService.GenerateCaseNoteDraftAsync(patientId, userId.Value);
                TempData["SuccessMessage"] = "AI case note draft generated. Review it below before it goes anywhere.";
            }
            catch (ClinicalAiException ex)
            {
                TempData["ErrorMessage"] = ex.Reason switch
                {
                    AiFailureReason.RateLimited => "The AI service is busy right now. Try again in a moment.",
                    AiFailureReason.Unauthorized => "The AI service rejected the request - check the configured API key.",
                    AiFailureReason.Timeout => "The AI service took too long to respond. Try again.",
                    AiFailureReason.InvalidResponse => ex.Message,
                    _ => "The AI service is unavailable right now. Nothing was changed."
                };
            }

            return RedirectToAction("Details", "Patients", new { id = patientId });
        }

        // POST: AiReview/Accept/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Accept(int id)
        {
            var userId = CurrentUserId();
            if (userId == null) return Forbid();

            var suggestion = await _context.AiSuggestions.FindAsync(id);
            if (suggestion == null) return NotFound();

            suggestion.Verdict = AiSuggestionVerdict.Accepted;
            suggestion.ReviewedById = userId.Value;
            suggestion.ReviewedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "AI suggestion accepted.";
            return RedirectToAction("Details", "Patients", new { id = suggestion.PatientId });
        }

        // POST: AiReview/Reject/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Reject(int id)
        {
            var userId = CurrentUserId();
            if (userId == null) return Forbid();

            var suggestion = await _context.AiSuggestions.FindAsync(id);
            if (suggestion == null) return NotFound();

            suggestion.Verdict = AiSuggestionVerdict.Rejected;
            suggestion.ReviewedById = userId.Value;
            suggestion.ReviewedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "AI suggestion rejected.";
            return RedirectToAction("Details", "Patients", new { id = suggestion.PatientId });
        }

        // POST: AiReview/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, string editedPayloadJson)
        {
            var userId = CurrentUserId();
            if (userId == null) return Forbid();

            var suggestion = await _context.AiSuggestions.FindAsync(id);
            if (suggestion == null) return NotFound();

            suggestion.Verdict = AiSuggestionVerdict.Edited;
            suggestion.EditedPayloadJson = editedPayloadJson;
            suggestion.ReviewedById = userId.Value;
            suggestion.ReviewedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Edited AI suggestion saved.";
            return RedirectToAction("Details", "Patients", new { id = suggestion.PatientId });
        }
    }
}
