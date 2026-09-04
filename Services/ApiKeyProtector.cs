using Microsoft.AspNetCore.DataProtection;

namespace HospitalManagementSystem.Services
{
    // Encrypts AI provider API keys at rest via ASP.NET Core's Data Protection API.
    // Purpose string is versioned so a future key-format change can add a new
    // protector without breaking old ciphertext still sitting in the database.
    public class ApiKeyProtector
    {
        private readonly IDataProtector _protector;

        public ApiKeyProtector(IDataProtectionProvider provider)
        {
            _protector = provider.CreateProtector("HospitalManagementSystem.AiProviderApiKeys.v1");
        }

        public string Protect(string plaintext) => _protector.Protect(plaintext);

        public string Unprotect(string ciphertext) => _protector.Unprotect(ciphertext);
    }
}
