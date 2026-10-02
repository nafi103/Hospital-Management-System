using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HospitalManagementSystem.Hubs;
using HospitalManagementSystem.Models;
using HospitalManagementSystem.Services;
using HospitalManagementSystem.Services.Contracts;
using HospitalManagementSystem.Services.Providers;
using HospitalManagementSystem.Tests.Fixtures;
using HospitalManagementSystem.Tests.Helpers;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace HospitalManagementSystem.Tests.Ai
{
    public class AiResponseSanitizationTests
    {
        private async Task<(ClinicalAiService Service, TestHubClients HubClients, ApplicationDbContext Context, ApiKeyProtector Protector)> CreateTestRigAsync(
            params IAiTextProvider[] providers)
        {
            var context = await TestDbContextFactory.CreateSeededDbContextAsync();
            var protector = new ApiKeyProtector(new EphemeralDataProtectionProvider());
            var cache = new MemoryCache(new MemoryCacheOptions());
            var scrubber = new PhiScrubber();
            var hubContext = new TestHubContext<AiStreamHub>();

            context.MedicalRecords.AddRange(
                new MedicalRecord
                {
                    Id = 1,
                    PatientId = 100,
                    DoctorId = 10,
                    Diagnosis = "Asthma flare",
                    Treatment = "Salbutamol nebulizer",
                    RecordedAt = DateTime.UtcNow.AddDays(-2),
                    CreatedAt = DateTime.UtcNow.AddDays(-2),
                    UpdatedAt = DateTime.UtcNow.AddDays(-2)
                },
                new MedicalRecord
                {
                    Id = 2,
                    PatientId = 100,
                    DoctorId = 10,
                    Diagnosis = "Follow-up",
                    Treatment = "Inhaler maintenance",
                    RecordedAt = DateTime.UtcNow.AddDays(-1),
                    CreatedAt = DateTime.UtcNow.AddDays(-1),
                    UpdatedAt = DateTime.UtcNow.AddDays(-1)
                }
            );
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
        public async Task CaseSummary_HallucinatedRecordCitation_IsStrippedFromFinalTextAndCitedList()
        {
            var provider = new TestAiTextProvider
            {
                Type = AiProviderType.Groq,
                StreamFunc = (key, model, sys, user, ct) => YieldChunks(
                    new AiStreamChunk(
                        "**Asthma follow-up**\n- Diagnosed with asthma [[rec:1]]\n- Followed up on inhaler [[rec:2]]\n- Fabricated event [[rec:9999]]",
                        model, 25, 15, 0)
                )
            };

            var (service, hubClients, context, protector) = await CreateTestRigAsync(provider);

            context.AiProviderSettings.Add(new AiProviderSetting
            {
                Provider = AiProviderType.Groq,
                ModelId = "llama-3.3",
                EncryptedApiKey = protector.Protect("key"),
                Priority = 1,
                IsEnabled = true
            });
            await context.SaveChangesAsync();

            var streamId = Guid.NewGuid().ToString("N");
            var suggestion = await service.GenerateCaseSummaryAsync(patientId: 100, requestedByUserId: 10, streamId: streamId);

            Assert.NotNull(suggestion);
            var draft = JsonSerializer.Deserialize<CaseSummaryDraft>(suggestion.PayloadJson);
            Assert.NotNull(draft);

            // Valid citations kept
            Assert.Contains("[[rec:1]]", draft.NarrativeText);
            Assert.Contains("[[rec:2]]", draft.NarrativeText);

            // Hallucinated citation stripped from narrative
            Assert.DoesNotContain("[[rec:9999]]", draft.NarrativeText);

            // CitedRecordIds only contains valid records
            Assert.Equal(2, draft.CitedRecordIds.Count);
            Assert.Contains(1, draft.CitedRecordIds);
            Assert.Contains(2, draft.CitedRecordIds);
            Assert.DoesNotContain(9999, draft.CitedRecordIds);
        }

        [Fact]
        public async Task CaseSummary_DuplicateCitations_AreDeduplicatedInCitedList()
        {
            var provider = new TestAiTextProvider
            {
                Type = AiProviderType.Groq,
                StreamFunc = (key, model, sys, user, ct) => YieldChunks(
                    new AiStreamChunk(
                        "**Asthma review**\n- Note A [[rec:1]]\n- Note B [[rec:1]]",
                        model, 20, 10, 0)
                )
            };

            var (service, hubClients, context, protector) = await CreateTestRigAsync(provider);

            context.AiProviderSettings.Add(new AiProviderSetting
            {
                Provider = AiProviderType.Groq,
                ModelId = "llama-3.3",
                EncryptedApiKey = protector.Protect("key"),
                Priority = 1,
                IsEnabled = true
            });
            await context.SaveChangesAsync();

            var streamId = Guid.NewGuid().ToString("N");
            var suggestion = await service.GenerateCaseSummaryAsync(patientId: 100, requestedByUserId: 10, streamId: streamId);

            var draft = JsonSerializer.Deserialize<CaseSummaryDraft>(suggestion.PayloadJson);
            Assert.NotNull(draft);
            Assert.Single(draft.CitedRecordIds);
            Assert.Equal(1, draft.CitedRecordIds[0]);
        }

        [Fact]
        public async Task CaseSummary_EmptyModelResponse_ThrowsInvalidResponseAndEmitsStreamError()
        {
            var provider = new TestAiTextProvider
            {
                Type = AiProviderType.Groq,
                StreamFunc = (key, model, sys, user, ct) => YieldChunks(
                    new AiStreamChunk("   \n\t   ", model, 10, 0, 0)
                )
            };

            var (service, hubClients, context, protector) = await CreateTestRigAsync(provider);

            context.AiProviderSettings.Add(new AiProviderSetting
            {
                Provider = AiProviderType.Groq,
                ModelId = "llama-3.3",
                EncryptedApiKey = protector.Protect("key"),
                Priority = 1,
                IsEnabled = true
            });
            await context.SaveChangesAsync();

            var streamId = Guid.NewGuid().ToString("N");
            var ex = await Assert.ThrowsAsync<ClinicalAiException>(() =>
                service.GenerateCaseSummaryAsync(patientId: 100, requestedByUserId: 10, streamId: streamId));

            Assert.Equal(AiFailureReason.InvalidResponse, ex.Reason);
            Assert.Contains("empty response", ex.Message);

            var groupProxy = hubClients.GetGroupProxy($"AiStream_{streamId}");
            var errorCall = Assert.Single(groupProxy.Invocations, i => i.Method == "StreamError");
            Assert.Contains("empty response", errorCall.Args[0]?.ToString());
        }

        [Fact]
        public void PhiScrubber_Scrub_MasksNameContactAndPhoneNumbers()
        {
            var scrubber = new PhiScrubber();
            var patient = new Patient
            {
                FullName = "Rahim Uddin",
                Uhid = "PT-8899",
                EmergencyContactName = "Fatema Begum",
                EmergencyContactPhone = 1712345678
            };

            var input = "Patient Rahim Uddin presented with cough. Emergency contact Fatema Begum was called at 01712345678.";
            var result = scrubber.Scrub(input, patient);

            Assert.DoesNotContain("Rahim Uddin", result.ScrubbedText);
            Assert.DoesNotContain("Fatema Begum", result.ScrubbedText);
            Assert.DoesNotContain("01712345678", result.ScrubbedText);

            Assert.Contains("Patient-PT-8899", result.ScrubbedText);
            Assert.Contains("Contact-PT-8899", result.ScrubbedText);
            Assert.Contains("[phone-redacted]", result.ScrubbedText);

            Assert.Equal("Rahim Uddin", result.Map["Patient-PT-8899"]);
            Assert.Equal("Fatema Begum", result.Map["Contact-PT-8899"]);
        }

        [Fact]
        public void PhiScrubber_Rehydrate_RestoresRealNames()
        {
            var scrubber = new PhiScrubber();
            var map = new Dictionary<string, string>
            {
                ["Patient-PT-8899"] = "Rahim Uddin",
                ["Contact-PT-8899"] = "Fatema Begum"
            };

            var pseudonymText = "Patient-PT-8899 has improved. Contact-PT-8899 advised on dosage.";
            var restored = scrubber.Rehydrate(pseudonymText, map);

            Assert.Equal("Rahim Uddin has improved. Fatema Begum advised on dosage.", restored);
        }

        [Fact]
        public void PhiScrubber_WholeWordMatching_DoesNotCorruptSubstrings()
        {
            var scrubber = new PhiScrubber();
            var patient = new Patient
            {
                FullName = "Ali",
                Uhid = "PT-1234"
            };

            var input = "Dr. Alia reviewed patient Ali today in Clinic.";
            var result = scrubber.Scrub(input, patient);

            Assert.Contains("Dr. Alia", result.ScrubbedText);
            Assert.Contains("Patient-PT-1234", result.ScrubbedText);
            Assert.DoesNotContain("Dr. Patient-PT-1234a", result.ScrubbedText);
        }

        [Fact]
        public async Task CaseSummary_EndToEnd_RehydratesPseudonymInFinalDraft()
        {
            var provider = new TestAiTextProvider
            {
                Type = AiProviderType.Groq,
                StreamFunc = (key, model, sys, user, ct) => YieldChunks(
                    new AiStreamChunk(
                        "**Assessment for Patient-PT-202610-0100**\n- Patient-PT-202610-0100 is responding well to therapy [[rec:1]]",
                        model, 25, 12, 0)
                )
            };

            var (service, hubClients, context, protector) = await CreateTestRigAsync(provider);

            context.AiProviderSettings.Add(new AiProviderSetting
            {
                Provider = AiProviderType.Groq,
                ModelId = "llama-3.3",
                EncryptedApiKey = protector.Protect("key"),
                Priority = 1,
                IsEnabled = true
            });
            await context.SaveChangesAsync();

            var streamId = Guid.NewGuid().ToString("N");
            var suggestion = await service.GenerateCaseSummaryAsync(patientId: 100, requestedByUserId: 10, streamId: streamId);

            var draft = JsonSerializer.Deserialize<CaseSummaryDraft>(suggestion.PayloadJson);
            Assert.NotNull(draft);

            // Rehydration check: "Patient-PT-202610-0100" -> "John Doe"
            Assert.Contains("Assessment for John Doe", draft.NarrativeText);
            Assert.Contains("John Doe is responding well", draft.NarrativeText);
            Assert.DoesNotContain("Patient-PT-202610-0100", draft.NarrativeText);
        }
    }
}
