using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using HospitalManagementSystem.Models;

namespace HospitalManagementSystem.Services.Providers
{
    public record ResolvedProvider(IAiTextProvider Provider, string ApiKey, string ModelId);

    // Replaces the old hardcoded "try Gemini then Groq" chain: reads enabled
    // AiProviderSetting rows in admin-assigned priority order and pairs each with its
    // IAiTextProvider implementation. Adding a new vendor is: implement IAiTextProvider,
    // register it in DI, done - no other code branches on provider type.
    public class AiProviderResolver
    {
        private readonly ApplicationDbContext _context;
        private readonly ApiKeyProtector _protector;
        private readonly IEnumerable<IAiTextProvider> _providers;

        public AiProviderResolver(ApplicationDbContext context, ApiKeyProtector protector, IEnumerable<IAiTextProvider> providers)
        {
            _context = context;
            _protector = protector;
            _providers = providers;
        }

        public async Task<List<ResolvedProvider>> GetOrderedProvidersAsync(CancellationToken ct)
        {
            var settings = await _context.AiProviderSettings
                .Where(s => s.IsEnabled)
                .OrderBy(s => s.Priority)
                .ToListAsync(ct);

            var resolved = new List<ResolvedProvider>();
            foreach (var setting in settings)
            {
                var impl = _providers.FirstOrDefault(p => p.Type == setting.Provider);
                if (impl == null || string.IsNullOrEmpty(setting.EncryptedApiKey))
                {
                    continue;
                }
                var apiKey = _protector.Unprotect(setting.EncryptedApiKey);
                resolved.Add(new ResolvedProvider(impl, apiKey, setting.ModelId));
            }
            return resolved;
        }
    }
}
