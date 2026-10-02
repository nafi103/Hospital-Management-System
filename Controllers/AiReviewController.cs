using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HospitalManagementSystem.Models;
using HospitalManagementSystem.Services;

namespace HospitalManagementSystem.Controllers
{
    [Authorize(Roles = "Doctor")]
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

        // POST: AiReview/GenerateCaseSummary
        // Called via fetch(), not a form post: the page stays put while ClinicalAiService
        // streams the narrative straight to the browser over AiStreamHub, and this action
        // just returns the finished suggestion's id once the stream ends.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GenerateCaseSummary(int patientId, string streamId)
        {
            var userId = CurrentUserId();
            if (userId == null) return Forbid();

            if (string.IsNullOrWhiteSpace(streamId))
            {
                return BadRequest(new { error = "Missing stream id." });
            }

            try
            {
                var suggestion = await _aiService.GenerateCaseSummaryAsync(patientId, userId.Value, streamId);
                return Json(new { suggestionId = suggestion.Id });
            }
            catch (ClinicalAiException ex)
            {
                var message = ex.Reason switch
                {
                    AiFailureReason.RateLimited => "The AI service is busy right now. Try again in a moment.",
                    AiFailureReason.Unauthorized => "The AI service rejected the request - check the configured API key.",
                    AiFailureReason.Timeout => "The AI service took too long to respond. Try again.",
                    AiFailureReason.InvalidResponse => ex.Message,
                    _ => "The AI service is unavailable right now. Nothing was changed."
                };
                return StatusCode(502, new { error = message });
            }
        }

        // POST: AiReview/GeneratePatientInstructions
        // Same fetch()-driven streaming pattern as GenerateCaseSummary, but keyed on a
        // prescription rather than a patient's record history - see
        // ClinicalAiService.GeneratePatientInstructionsAsync.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GeneratePatientInstructions(int prescriptionId, string streamId)
        {
            var userId = CurrentUserId();
            if (userId == null) return Forbid();

            if (string.IsNullOrWhiteSpace(streamId))
            {
                return BadRequest(new { error = "Missing stream id." });
            }

            try
            {
                var suggestion = await _aiService.GeneratePatientInstructionsAsync(prescriptionId, userId.Value, streamId);
                return Json(new { suggestionId = suggestion.Id });
            }
            catch (ClinicalAiException ex)
            {
                var message = ex.Reason switch
                {
                    AiFailureReason.RateLimited => "The AI service is busy right now. Try again in a moment.",
                    AiFailureReason.Unauthorized => "The AI service rejected the request - check the configured API key.",
                    AiFailureReason.Timeout => "The AI service took too long to respond. Try again.",
                    AiFailureReason.InvalidResponse => ex.Message,
                    _ => "The AI service is unavailable right now. Nothing was changed."
                };
                return StatusCode(502, new { error = message });
            }
        }

        // GET: AiReview/RenderSuggestion/5
        // Returns the rendered suggestion card so the browser can swap a live
        // placeholder for it. `compact=true` (used by the doctor dashboard's inline AI
        // review panel) skips the outer card/header and renders just the narrative +
        // Accept/Edit/Reject controls; the default renders the full card used on the
        // patient page.
        [HttpGet]
        public async Task<IActionResult> RenderSuggestion(int id, bool compact = false, string? returnUrl = null)
        {
            var suggestion = await _context.AiSuggestions
                .Include(s => s.ReviewedBy)
                .FirstOrDefaultAsync(s => s.Id == id);
            if (suggestion == null) return NotFound();

            // Honored regardless of compact/full rendering - without it, Accept/Edit/Reject
            // on a suggestion rendered anywhere other than the patient page (e.g. the
            // prescription page's patient-instructions card) would redirect back to
            // Patients/Details by default, which is the wrong page for that flow.
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                ViewData["ReturnUrl"] = returnUrl;
            }

            if (compact)
            {
                return PartialView("_SuggestionCardBody", suggestion);
            }
            return PartialView("_SuggestionCard", suggestion);
        }

        // A redirect target is only honored when it's a same-app local URL - Url.IsLocalUrl
        // rejects "//evil.com" and absolute URLs, so a crafted returnUrl can't be used to
        // redirect a doctor off the site after an Accept/Reject/Edit POST.
        private IActionResult RedirectAfterReview(string? returnUrl, int patientId)
        {
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }
            return RedirectToAction("Details", "Patients", new { id = patientId });
        }

        // POST: AiReview/Accept/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Accept(int id, string? returnUrl)
        {
            var userId = CurrentUserId();
            if (userId == null) return Forbid();

            var suggestion = await _context.AiSuggestions.FindAsync(id);
            if (suggestion == null) return NotFound();

            var now = DateTime.UtcNow;
            int rowsAffected;
            if (_context.Database.IsRelational())
            {
                rowsAffected = await _context.AiSuggestions
                    .Where(s => s.Id == id && s.Verdict == AiSuggestionVerdict.Pending)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(s => s.Verdict, AiSuggestionVerdict.Accepted)
                        .SetProperty(s => s.ReviewedById, userId.Value)
                        .SetProperty(s => s.ReviewedAt, now));
            }
            else
            {
                var target = await _context.AiSuggestions
                    .FirstOrDefaultAsync(s => s.Id == id && s.Verdict == AiSuggestionVerdict.Pending);
                if (target != null)
                {
                    target.Verdict = AiSuggestionVerdict.Accepted;
                    target.ReviewedById = userId.Value;
                    target.ReviewedAt = now;
                    await _context.SaveChangesAsync();
                    rowsAffected = 1;
                }
                else
                {
                    rowsAffected = 0;
                }
            }

            if (rowsAffected == 0)
            {
                var current = await _context.AiSuggestions.FindAsync(id);
                TempData["ErrorMessage"] = $"This AI suggestion has already been reviewed ({current?.Verdict}) and cannot be modified.";
                return RedirectAfterReview(returnUrl, suggestion.PatientId);
            }

            TempData["SuccessMessage"] = "AI suggestion accepted.";
            return RedirectAfterReview(returnUrl, suggestion.PatientId);
        }

        // POST: AiReview/Reject/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Reject(int id, string? returnUrl)
        {
            var userId = CurrentUserId();
            if (userId == null) return Forbid();

            var suggestion = await _context.AiSuggestions.FindAsync(id);
            if (suggestion == null) return NotFound();

            var now = DateTime.UtcNow;
            int rowsAffected;
            if (_context.Database.IsRelational())
            {
                rowsAffected = await _context.AiSuggestions
                    .Where(s => s.Id == id && s.Verdict == AiSuggestionVerdict.Pending)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(s => s.Verdict, AiSuggestionVerdict.Rejected)
                        .SetProperty(s => s.ReviewedById, userId.Value)
                        .SetProperty(s => s.ReviewedAt, now));
            }
            else
            {
                var target = await _context.AiSuggestions
                    .FirstOrDefaultAsync(s => s.Id == id && s.Verdict == AiSuggestionVerdict.Pending);
                if (target != null)
                {
                    target.Verdict = AiSuggestionVerdict.Rejected;
                    target.ReviewedById = userId.Value;
                    target.ReviewedAt = now;
                    await _context.SaveChangesAsync();
                    rowsAffected = 1;
                }
                else
                {
                    rowsAffected = 0;
                }
            }

            if (rowsAffected == 0)
            {
                var current = await _context.AiSuggestions.FindAsync(id);
                TempData["ErrorMessage"] = $"This AI suggestion has already been reviewed ({current?.Verdict}) and cannot be modified.";
                return RedirectAfterReview(returnUrl, suggestion.PatientId);
            }

            TempData["SuccessMessage"] = "AI suggestion rejected.";
            return RedirectAfterReview(returnUrl, suggestion.PatientId);
        }

        // POST: AiReview/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, string editedPayloadJson, string? returnUrl)
        {
            var userId = CurrentUserId();
            if (userId == null) return Forbid();

            if (string.IsNullOrWhiteSpace(editedPayloadJson))
            {
                TempData["ErrorMessage"] = "Edited payload content cannot be empty.";
                var existing = await _context.AiSuggestions.FindAsync(id);
                return existing != null ? RedirectAfterReview(returnUrl, existing.PatientId) : NotFound();
            }

            var suggestion = await _context.AiSuggestions.FindAsync(id);
            if (suggestion == null) return NotFound();

            var now = DateTime.UtcNow;
            int rowsAffected;
            if (_context.Database.IsRelational())
            {
                rowsAffected = await _context.AiSuggestions
                    .Where(s => s.Id == id && s.Verdict == AiSuggestionVerdict.Pending)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(s => s.Verdict, AiSuggestionVerdict.Edited)
                        .SetProperty(s => s.EditedPayloadJson, editedPayloadJson)
                        .SetProperty(s => s.ReviewedById, userId.Value)
                        .SetProperty(s => s.ReviewedAt, now));
            }
            else
            {
                var target = await _context.AiSuggestions
                    .FirstOrDefaultAsync(s => s.Id == id && s.Verdict == AiSuggestionVerdict.Pending);
                if (target != null)
                {
                    target.Verdict = AiSuggestionVerdict.Edited;
                    target.EditedPayloadJson = editedPayloadJson;
                    target.ReviewedById = userId.Value;
                    target.ReviewedAt = now;
                    await _context.SaveChangesAsync();
                    rowsAffected = 1;
                }
                else
                {
                    rowsAffected = 0;
                }
            }

            if (rowsAffected == 0)
            {
                var current = await _context.AiSuggestions.FindAsync(id);
                TempData["ErrorMessage"] = $"This AI suggestion has already been reviewed ({current?.Verdict}) and cannot be modified.";
                return RedirectAfterReview(returnUrl, suggestion.PatientId);
            }

            TempData["SuccessMessage"] = "Edited AI suggestion saved.";
            return RedirectAfterReview(returnUrl, suggestion.PatientId);
        }
    }
}
