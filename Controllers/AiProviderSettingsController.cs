using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HospitalManagementSystem.Models;
using HospitalManagementSystem.Services;

namespace HospitalManagementSystem.Controllers
{
    public class AiProviderRow
    {
        public AiProviderType Provider { get; set; }
        public int? Id { get; set; }
        public bool IsConfigured => Id.HasValue;
        public string ModelId { get; set; } = string.Empty;
        public bool IsEnabled { get; set; }
        public int Priority { get; set; }
    }

    [Authorize(Roles = "Admin")]
    public class AiProviderSettingsController : Controller
    {
        // Suggested defaults shown as placeholder text when a provider has never been
        // configured - purely a UI convenience, not read anywhere else.
        private static readonly Dictionary<AiProviderType, string> DefaultModelSuggestion = new()
        {
            [AiProviderType.Gemini] = "gemini-3.8-flash",
            [AiProviderType.Groq] = "openai/gpt-oss-20b",
            [AiProviderType.Anthropic] = "claude-opus-5"
        };

        private readonly ApplicationDbContext _context;
        private readonly ApiKeyProtector _protector;

        public AiProviderSettingsController(ApplicationDbContext context, ApiKeyProtector protector)
        {
            _context = context;
            _protector = protector;
        }

        private int? CurrentUserId()
        {
            var claim = User.Claims.FirstOrDefault(c => c.Type == "UserId")?.Value;
            return int.TryParse(claim, out int id) ? id : null;
        }

        public async Task<IActionResult> Index()
        {
            var settings = await _context.AiProviderSettings.OrderBy(s => s.Priority).ToListAsync();

            var rows = settings.Select(s => new AiProviderRow
            {
                Provider = s.Provider,
                Id = s.Id,
                ModelId = s.ModelId,
                IsEnabled = s.IsEnabled,
                Priority = s.Priority
            }).ToList();

            foreach (AiProviderType type in Enum.GetValues<AiProviderType>())
            {
                if (rows.All(r => r.Provider != type))
                {
                    rows.Add(new AiProviderRow { Provider = type, ModelId = DefaultModelSuggestion[type] });
                }
            }

            ViewBag.DefaultModelSuggestion = DefaultModelSuggestion;
            return View(rows);
        }

        // POST: AiProviderSettings/Save
        // apiKey blank + already configured => keep the existing encrypted key, only
        // update model/enabled. apiKey filled => replace it. A brand-new provider is
        // appended after the current lowest priority (tried last by default).
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Save(AiProviderType provider, string? apiKey, string modelId, bool isEnabled)
        {
            if (string.IsNullOrWhiteSpace(modelId))
            {
                TempData["ErrorMessage"] = "Model id is required.";
                return RedirectToAction(nameof(Index));
            }

            var existing = await _context.AiProviderSettings.FirstOrDefaultAsync(s => s.Provider == provider);
            var userId = CurrentUserId();
            var now = DateTime.UtcNow;

            if (existing == null)
            {
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    TempData["ErrorMessage"] = "An API key is required to add a new provider.";
                    return RedirectToAction(nameof(Index));
                }

                var maxPriority = await _context.AiProviderSettings.Select(s => (int?)s.Priority).MaxAsync() ?? -1;
                _context.AiProviderSettings.Add(new AiProviderSetting
                {
                    Provider = provider,
                    EncryptedApiKey = _protector.Protect(apiKey),
                    ModelId = modelId.Trim(),
                    IsEnabled = isEnabled,
                    Priority = maxPriority + 1,
                    UpdatedById = userId,
                    CreatedAt = now,
                    UpdatedAt = now
                });
                TempData["SuccessMessage"] = $"{provider} added.";
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    existing.EncryptedApiKey = _protector.Protect(apiKey);
                }
                existing.ModelId = modelId.Trim();
                existing.IsEnabled = isEnabled;
                existing.UpdatedById = userId;
                existing.UpdatedAt = now;
                TempData["SuccessMessage"] = $"{provider} updated.";
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        // POST: AiProviderSettings/Remove/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Remove(int id)
        {
            var setting = await _context.AiProviderSettings.FindAsync(id);
            if (setting == null) return NotFound();

            _context.AiProviderSettings.Remove(setting);
            await _context.SaveChangesAsync();
            await NormalizePrioritiesAsync();

            TempData["SuccessMessage"] = $"{setting.Provider} removed.";
            return RedirectToAction(nameof(Index));
        }

        // POST: AiProviderSettings/MoveUp/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MoveUp(int id)
        {
            await SwapWithNeighborAsync(id, moveUp: true);
            return RedirectToAction(nameof(Index));
        }

        // POST: AiProviderSettings/MoveDown/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MoveDown(int id)
        {
            await SwapWithNeighborAsync(id, moveUp: false);
            return RedirectToAction(nameof(Index));
        }

        private async Task SwapWithNeighborAsync(int id, bool moveUp)
        {
            var ordered = await _context.AiProviderSettings.OrderBy(s => s.Priority).ToListAsync();
            var index = ordered.FindIndex(s => s.Id == id);
            if (index < 0) return;

            var neighborIndex = moveUp ? index - 1 : index + 1;
            if (neighborIndex < 0 || neighborIndex >= ordered.Count) return;

            (ordered[index].Priority, ordered[neighborIndex].Priority) = (ordered[neighborIndex].Priority, ordered[index].Priority);
            await _context.SaveChangesAsync();
        }

        // Closes any gap left by a removal so priorities stay a clean 0,1,2,... sequence.
        private async Task NormalizePrioritiesAsync()
        {
            var ordered = await _context.AiProviderSettings.OrderBy(s => s.Priority).ToListAsync();
            for (var i = 0; i < ordered.Count; i++)
            {
                ordered[i].Priority = i;
            }
            await _context.SaveChangesAsync();
        }
    }
}
