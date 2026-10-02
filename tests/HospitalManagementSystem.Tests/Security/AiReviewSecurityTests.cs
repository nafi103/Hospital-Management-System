using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HospitalManagementSystem.Controllers;
using HospitalManagementSystem.Models;
using HospitalManagementSystem.Services;
using HospitalManagementSystem.Tests.Fixtures;
using HospitalManagementSystem.Tests.Helpers;
using Xunit;

namespace HospitalManagementSystem.Tests.Security
{
    public class AiReviewSecurityTests
    {
        private class DummyClinicalAiService : IClinicalAiService
        {
            public Task<AiSuggestion> GenerateCaseSummaryAsync(int patientId, int requestedByUserId, string streamId, CancellationToken ct = default) =>
                Task.FromResult(new AiSuggestion { Id = 999, PatientId = patientId });

            public Task<AiSuggestion> GeneratePatientInstructionsAsync(int prescriptionId, int requestedByUserId, string streamId, CancellationToken ct = default) =>
                Task.FromResult(new AiSuggestion { Id = 999, PatientId = 100 });
        }

        private async Task<(ApplicationDbContext Context, AiReviewController Controller)> CreateTestSetupAsync(ClaimsPrincipal user)
        {
            var context = await TestDbContextFactory.CreateSeededDbContextAsync();
            var controller = new AiReviewController(context, new DummyClinicalAiService());
            ControllerTestHelper.SetupController(controller, user);
            return (context, controller);
        }

        [Fact]
        public async Task Accept_ValidPendingSuggestion_TransitionsToAccepted()
        {
            // Arrange
            var (context, controller) = await CreateTestSetupAsync(TestPrincipalFactory.CreateDoctor(doctorId: 10, username: "dr_alice"));

            var suggestion = new AiSuggestion
            {
                Id = 10,
                PatientId = 100,
                SuggestionType = AiSuggestionType.CaseSummary,
                PayloadJson = "{\"summary\": \"Initial draft\"}",
                Verdict = AiSuggestionVerdict.Pending,
                CreatedAt = DateTime.UtcNow
            };
            context.AiSuggestions.Add(suggestion);
            await context.SaveChangesAsync();

            // Act
            var result = await controller.Accept(10, returnUrl: null);

            // Assert
            var redirectResult = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Details", redirectResult.ActionName);
            Assert.Equal("Patients", redirectResult.ControllerName);
            Assert.Equal("AI suggestion accepted.", controller.TempData["SuccessMessage"]);

            var updated = await context.AiSuggestions.FindAsync(10);
            Assert.NotNull(updated);
            Assert.Equal(AiSuggestionVerdict.Accepted, updated.Verdict);
            Assert.Equal(10, updated.ReviewedById);
            Assert.NotNull(updated.ReviewedAt);
        }

        [Fact]
        public async Task Reject_ValidPendingSuggestion_TransitionsToRejected()
        {
            // Arrange
            var (context, controller) = await CreateTestSetupAsync(TestPrincipalFactory.CreateDoctor(doctorId: 10, username: "dr_alice"));

            var suggestion = new AiSuggestion
            {
                Id = 11,
                PatientId = 100,
                SuggestionType = AiSuggestionType.CaseSummary,
                PayloadJson = "{\"summary\": \"Inaccurate draft\"}",
                Verdict = AiSuggestionVerdict.Pending,
                CreatedAt = DateTime.UtcNow
            };
            context.AiSuggestions.Add(suggestion);
            await context.SaveChangesAsync();

            // Act
            var result = await controller.Reject(11, returnUrl: null);

            // Assert
            var redirectResult = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("AI suggestion rejected.", controller.TempData["SuccessMessage"]);

            var updated = await context.AiSuggestions.FindAsync(11);
            Assert.NotNull(updated);
            Assert.Equal(AiSuggestionVerdict.Rejected, updated.Verdict);
            Assert.Equal(10, updated.ReviewedById);
            Assert.NotNull(updated.ReviewedAt);
        }

        [Fact]
        public async Task Edit_ValidPendingSuggestion_TransitionsToEdited()
        {
            // Arrange
            var (context, controller) = await CreateTestSetupAsync(TestPrincipalFactory.CreateDoctor(doctorId: 10, username: "dr_alice"));

            var suggestion = new AiSuggestion
            {
                Id = 12,
                PatientId = 100,
                SuggestionType = AiSuggestionType.CaseSummary,
                PayloadJson = "{\"summary\": \"Draft\"}",
                Verdict = AiSuggestionVerdict.Pending,
                CreatedAt = DateTime.UtcNow
            };
            context.AiSuggestions.Add(suggestion);
            await context.SaveChangesAsync();

            // Act
            var editedJson = "{\"summary\": \"Doctor-corrected clinical narrative\"}";
            var result = await controller.Edit(12, editedJson, returnUrl: null);

            // Assert
            var redirectResult = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Edited AI suggestion saved.", controller.TempData["SuccessMessage"]);

            var updated = await context.AiSuggestions.FindAsync(12);
            Assert.NotNull(updated);
            Assert.Equal(AiSuggestionVerdict.Edited, updated.Verdict);
            Assert.Equal(editedJson, updated.EditedPayloadJson);
            Assert.Equal(10, updated.ReviewedById);
            Assert.NotNull(updated.ReviewedAt);
        }

        [Fact]
        public async Task Edit_EmptyPayload_IsRejectedWithErrorMessage()
        {
            // Arrange
            var (context, controller) = await CreateTestSetupAsync(TestPrincipalFactory.CreateDoctor(doctorId: 10, username: "dr_alice"));

            var suggestion = new AiSuggestion
            {
                Id = 13,
                PatientId = 100,
                SuggestionType = AiSuggestionType.CaseSummary,
                PayloadJson = "{\"summary\": \"Draft\"}",
                Verdict = AiSuggestionVerdict.Pending,
                CreatedAt = DateTime.UtcNow
            };
            context.AiSuggestions.Add(suggestion);
            await context.SaveChangesAsync();

            // Act: Submit empty string
            var result = await controller.Edit(13, "   ", returnUrl: null);

            // Assert
            var redirectResult = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Edited payload content cannot be empty.", controller.TempData["ErrorMessage"]);

            var dbRow = await context.AiSuggestions.FindAsync(13);
            Assert.NotNull(dbRow);
            Assert.Equal(AiSuggestionVerdict.Pending, dbRow.Verdict);
        }

        [Fact]
        public async Task Accept_AlreadyAcceptedSuggestion_IsRejectedByStateMachine()
        {
            // Arrange: Suggestion is already Accepted
            var (context, controller) = await CreateTestSetupAsync(TestPrincipalFactory.CreateDoctor(doctorId: 10, username: "dr_alice"));

            var suggestion = new AiSuggestion
            {
                Id = 14,
                PatientId = 100,
                SuggestionType = AiSuggestionType.CaseSummary,
                PayloadJson = "{\"summary\": \"Accepted draft\"}",
                Verdict = AiSuggestionVerdict.Accepted,
                ReviewedById = 10,
                ReviewedAt = DateTime.UtcNow.AddMinutes(-30),
                CreatedAt = DateTime.UtcNow.AddHours(-1)
            };
            context.AiSuggestions.Add(suggestion);
            await context.SaveChangesAsync();

            // Act: Try to accept again
            var result = await controller.Accept(14, returnUrl: null);

            // Assert: Rejected by state machine
            var redirectResult = Assert.IsType<RedirectToActionResult>(result);
            var error = controller.TempData["ErrorMessage"]?.ToString();
            Assert.NotNull(error);
            Assert.Contains("already been reviewed (Accepted) and cannot be modified", error);
        }

        [Fact]
        public async Task Reject_AlreadyRejectedSuggestion_IsRejectedByStateMachine()
        {
            // Arrange: Suggestion is already Rejected
            var (context, controller) = await CreateTestSetupAsync(TestPrincipalFactory.CreateDoctor(doctorId: 10, username: "dr_alice"));

            var suggestion = new AiSuggestion
            {
                Id = 15,
                PatientId = 100,
                SuggestionType = AiSuggestionType.CaseSummary,
                PayloadJson = "{\"summary\": \"Rejected draft\"}",
                Verdict = AiSuggestionVerdict.Rejected,
                ReviewedById = 10,
                ReviewedAt = DateTime.UtcNow.AddMinutes(-30),
                CreatedAt = DateTime.UtcNow.AddHours(-1)
            };
            context.AiSuggestions.Add(suggestion);
            await context.SaveChangesAsync();

            // Act: Try to reject again
            var result = await controller.Reject(15, returnUrl: null);

            // Assert: Rejected by state machine
            var redirectResult = Assert.IsType<RedirectToActionResult>(result);
            var error = controller.TempData["ErrorMessage"]?.ToString();
            Assert.NotNull(error);
            Assert.Contains("already been reviewed (Rejected) and cannot be modified", error);
        }

        [Fact]
        public async Task Accept_CallerWithoutUserIdClaim_ReturnsForbid()
        {
            // Arrange: Principal without UserId claim
            var claims = new[] { new Claim(ClaimTypes.Name, "invalid_user"), new Claim(ClaimTypes.Role, "Doctor") };
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));

            var (context, controller) = await CreateTestSetupAsync(principal);

            // Act
            var result = await controller.Accept(1, returnUrl: null);

            // Assert
            Assert.IsType<ForbidResult>(result);
        }
    }
}
