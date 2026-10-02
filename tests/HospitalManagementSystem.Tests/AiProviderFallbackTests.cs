using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HospitalManagementSystem.Hubs;
using HospitalManagementSystem.Models;
using HospitalManagementSystem.Services;
using HospitalManagementSystem.Services.Providers;
using HospitalManagementSystem.Tests.Fixtures;
using HospitalManagementSystem.Tests.Helpers;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace HospitalManagementSystem.Tests
{
    public class AiProviderFallbackTests
    {
        private async Task<(ClinicalAiService Service, TestHubClients HubClients, ApplicationDbContext Context, ApiKeyProtector Protector)> CreateTestRigAsync(
            IEnumerable<IAiTextProvider> providers)
        {
            var context = await TestDbContextFactory.CreateSeededDbContextAsync();
            var protector = new ApiKeyProtector(new EphemeralDataProtectionProvider());
            var cache = new MemoryCache(new MemoryCacheOptions());
            var scrubber = new PhiScrubber();
            var hubContext = new TestHubContext<AiStreamHub>();

            // Seed a medical record for patient 100 so context generation succeeds
            context.MedicalRecords.Add(new MedicalRecord
            {
                Id = 1,
                PatientId = 100,
                DoctorId = 10,
                Diagnosis = "Acute Bronchitis",
                Treatment = "Rest and hydration",
                RecordedAt = DateTime.UtcNow.AddDays(-1),
                CreatedAt = DateTime.UtcNow.AddDays(-1),
                UpdatedAt = DateTime.UtcNow.AddDays(-1)
            });
            await context.SaveChangesAsync();

            var resolver = new AiProviderResolver(context, protector, providers);
            var service = new ClinicalAiService(context, scrubber, cache, hubContext, resolver);

            return (service, (TestHubClients)hubContext.Clients, context, protector);
        }

        private static async IAsyncEnumerable<AiStreamChunk> YieldChunks(params AiStreamChunk[] chunks)
        {
            foreach (var chunk in chunks)
            {
                yield return chunk;
            }
        }

        [Fact]
        public async Task Fallback_PrimaryFails_SecondarySucceeds()
        {
            var groqProvider = new TestAiTextProvider
            {
                Type = AiProviderType.Groq,
                StreamFunc = (key, model, sys, user, ct) => throw new ClinicalAiException(AiFailureReason.Unknown, "Groq 502 Bad Gateway")
            };

            var geminiProvider = new TestAiTextProvider
            {
                Type = AiProviderType.Gemini,
                StreamFunc = (key, model, sys, user, ct) => YieldChunks(
                    new AiStreamChunk("**Acute Bronchitis Evaluation**\n- Diagnosed recently [[rec:1]]", model, 25, 10, 0))
            };

            var (service, hubClients, context, protector) = await CreateTestRigAsync(new[] { groqProvider, geminiProvider });

            // Priority 1: Groq, Priority 2: Gemini
            context.AiProviderSettings.AddRange(
                new AiProviderSetting { Provider = AiProviderType.Groq, ModelId = "llama-3.3", EncryptedApiKey = protector.Protect("key1"), Priority = 1, IsEnabled = true },
                new AiProviderSetting { Provider = AiProviderType.Gemini, ModelId = "gemini-2.5", EncryptedApiKey = protector.Protect("key2"), Priority = 2, IsEnabled = true }
            );
            await context.SaveChangesAsync();

            var streamId = Guid.NewGuid().ToString("N");
            var suggestion = await service.GenerateCaseSummaryAsync(patientId: 100, requestedByUserId: 10, streamId: streamId);

            Assert.NotNull(suggestion);
            Assert.Equal("gemini-2.5", suggestion.ModelId);
            Assert.Equal(AiSuggestionVerdict.Pending, suggestion.Verdict);
            Assert.Contains("Acute Bronchitis Evaluation", suggestion.PayloadJson);

            // Verify SignalR notifications sent to the stream group
            var groupProxy = hubClients.GetGroupProxy($"AiStream_{streamId}");
            var switchInvocations = groupProxy.Invocations.Where(i => i.Method == "StreamRestarted").ToList();
            Assert.NotEmpty(switchInvocations);
            Assert.Contains(switchInvocations, i => i.Args[0]?.ToString()?.Contains("Switching to backup AI provider (Gemini)") == true);
        }

        [Fact]
        public async Task Fallback_PrimaryAndSecondaryFail_TertiarySucceeds()
        {
            var groqProvider = new TestAiTextProvider
            {
                Type = AiProviderType.Groq,
                StreamFunc = (key, model, sys, user, ct) => throw new ClinicalAiException(AiFailureReason.Timeout, "Groq timed out")
            };

            var geminiProvider = new TestAiTextProvider
            {
                Type = AiProviderType.Gemini,
                StreamFunc = (key, model, sys, user, ct) => throw new ClinicalAiException(AiFailureReason.Unknown, "Gemini 500 error")
            };

            var anthropicProvider = new TestAiTextProvider
            {
                Type = AiProviderType.Anthropic,
                StreamFunc = (key, model, sys, user, ct) => YieldChunks(
                    new AiStreamChunk("**Tertiary Provider Summary**\n- Clinical notes [[rec:1]]", model, 30, 15, 0))
            };

            var (service, hubClients, context, protector) = await CreateTestRigAsync(new[] { groqProvider, geminiProvider, anthropicProvider });

            context.AiProviderSettings.AddRange(
                new AiProviderSetting { Provider = AiProviderType.Groq, ModelId = "llama-3.3", EncryptedApiKey = protector.Protect("key1"), Priority = 1, IsEnabled = true },
                new AiProviderSetting { Provider = AiProviderType.Gemini, ModelId = "gemini-2.5", EncryptedApiKey = protector.Protect("key2"), Priority = 2, IsEnabled = true },
                new AiProviderSetting { Provider = AiProviderType.Anthropic, ModelId = "claude-3-5", EncryptedApiKey = protector.Protect("key3"), Priority = 3, IsEnabled = true }
            );
            await context.SaveChangesAsync();

            var streamId = Guid.NewGuid().ToString("N");
            var suggestion = await service.GenerateCaseSummaryAsync(patientId: 100, requestedByUserId: 10, streamId: streamId);

            Assert.NotNull(suggestion);
            Assert.Equal("claude-3-5", suggestion.ModelId);
            Assert.Contains("Tertiary Provider Summary", suggestion.PayloadJson);

            var groupProxy = hubClients.GetGroupProxy($"AiStream_{streamId}");
            var switchInvocations = groupProxy.Invocations.Where(i => i.Method == "StreamRestarted").ToList();
            Assert.Contains(switchInvocations, i => i.Args[0]?.ToString()?.Contains("Switching to backup AI provider (Gemini)") == true);
            Assert.Contains(switchInvocations, i => i.Args[0]?.ToString()?.Contains("Switching to backup AI provider (Anthropic)") == true);
        }

        [Fact]
        public async Task Fallback_AllProvidersFail_ThrowsClinicalAiExceptionAndEmitsStreamError()
        {
            var groqProvider = new TestAiTextProvider
            {
                Type = AiProviderType.Groq,
                StreamFunc = (key, model, sys, user, ct) => throw new ClinicalAiException(AiFailureReason.Unknown, "Groq 500")
            };

            var geminiProvider = new TestAiTextProvider
            {
                Type = AiProviderType.Gemini,
                StreamFunc = (key, model, sys, user, ct) => throw new ClinicalAiException(AiFailureReason.Unknown, "Gemini 500")
            };

            var (service, hubClients, context, protector) = await CreateTestRigAsync(new[] { groqProvider, geminiProvider });

            context.AiProviderSettings.AddRange(
                new AiProviderSetting { Provider = AiProviderType.Groq, ModelId = "llama-3.3", EncryptedApiKey = protector.Protect("key1"), Priority = 1, IsEnabled = true },
                new AiProviderSetting { Provider = AiProviderType.Gemini, ModelId = "gemini-2.5", EncryptedApiKey = protector.Protect("key2"), Priority = 2, IsEnabled = true }
            );
            await context.SaveChangesAsync();

            var streamId = Guid.NewGuid().ToString("N");
            var ex = await Assert.ThrowsAsync<ClinicalAiException>(() =>
                service.GenerateCaseSummaryAsync(patientId: 100, requestedByUserId: 10, streamId: streamId));

            Assert.Equal(AiFailureReason.Unknown, ex.Reason);

            var groupProxy = hubClients.GetGroupProxy($"AiStream_{streamId}");
            var errorInvocation = Assert.Single(groupProxy.Invocations, i => i.Method == "StreamError");
            Assert.Contains("All configured AI providers are unavailable", errorInvocation.Args[0]?.ToString());

            // Assert no corrupt suggestion saved in database
            Assert.Empty(context.AiSuggestions);
        }

        [Fact]
        public async Task Fallback_NoConfiguredProviders_ThrowsUnauthorizedException()
        {
            var (service, hubClients, context, protector) = await CreateTestRigAsync(Array.Empty<IAiTextProvider>());

            // No settings in DB
            var streamId = Guid.NewGuid().ToString("N");
            var ex = await Assert.ThrowsAsync<ClinicalAiException>(() =>
                service.GenerateCaseSummaryAsync(patientId: 100, requestedByUserId: 10, streamId: streamId));

            Assert.Equal(AiFailureReason.Unauthorized, ex.Reason);
            Assert.Contains("No AI provider is configured", ex.Message);
        }

        [Fact]
        public async Task Fallback_PrimaryUnauthorized_ImmediatelySwitchesWithoutWastingRetries()
        {
            int groqCalls = 0;
            var groqProvider = new TestAiTextProvider
            {
                Type = AiProviderType.Groq,
                StreamFunc = (key, model, sys, user, ct) =>
                {
                    groqCalls++;
                    throw new ClinicalAiException(AiFailureReason.Unauthorized, "Invalid API key");
                }
            };

            var geminiProvider = new TestAiTextProvider
            {
                Type = AiProviderType.Gemini,
                StreamFunc = (key, model, sys, user, ct) => YieldChunks(
                    new AiStreamChunk("**Gemini Backup**\n- Treatment given [[rec:1]]", model, 20, 10, 0))
            };

            var (service, hubClients, context, protector) = await CreateTestRigAsync(new[] { groqProvider, geminiProvider });

            context.AiProviderSettings.AddRange(
                new AiProviderSetting { Provider = AiProviderType.Groq, ModelId = "llama-3.3", EncryptedApiKey = protector.Protect("bad_key"), Priority = 1, IsEnabled = true },
                new AiProviderSetting { Provider = AiProviderType.Gemini, ModelId = "gemini-2.5", EncryptedApiKey = protector.Protect("key2"), Priority = 2, IsEnabled = true }
            );
            await context.SaveChangesAsync();

            var streamId = Guid.NewGuid().ToString("N");
            var suggestion = await service.GenerateCaseSummaryAsync(patientId: 100, requestedByUserId: 10, streamId: streamId);

            Assert.NotNull(suggestion);
            Assert.Equal("gemini-2.5", suggestion.ModelId);
            // Groq should have only been attempted once (no retries because it's not RateLimited)
            Assert.Equal(1, groqCalls);
        }
    }
}
