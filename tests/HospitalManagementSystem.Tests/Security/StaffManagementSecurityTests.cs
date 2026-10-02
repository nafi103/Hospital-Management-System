using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HospitalManagementSystem.Controllers;
using HospitalManagementSystem.Models;
using HospitalManagementSystem.Tests.Fixtures;
using HospitalManagementSystem.Tests.Helpers;
using Xunit;

namespace HospitalManagementSystem.Tests.Security
{
    public class StaffManagementSecurityTests
    {
        private async Task<(ApplicationDbContext Context, StaffController Controller)> CreateTestSetupAsync(int currentAdminId = 1)
        {
            var context = await TestDbContextFactory.CreateSeededDbContextAsync();

            // Ensure baseline admin user exists
            var adminUser = await context.Users.FindAsync(1);
            if (adminUser == null)
            {
                var adminRole = await context.Roles.FirstOrDefaultAsync(r => r.RoleName == "Admin");
                adminUser = new User
                {
                    Id = 1,
                    Username = "admin",
                    FullName = "System Admin",
                    RoleId = adminRole!.Id,
                    Category = "Administrator",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("AdminPass123!"),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                context.Users.Add(adminUser);
                await context.SaveChangesAsync();
            }

            var controller = new StaffController(context);
            ControllerTestHelper.SetupController(controller, TestPrincipalFactory.CreateAdmin(adminId: currentAdminId, username: "admin"));
            return (context, controller);
        }

        [Fact]
        public async Task DeleteConfirmed_SelfDeletion_IsRejectedWithErrorMessage()
        {
            // Arrange: Admin 1 attempts to delete their own account
            var (context, controller) = await CreateTestSetupAsync(currentAdminId: 1);

            // Act: Attempt to delete self
            var result = await controller.DeleteConfirmed(1);

            // Assert: Deletion is rejected
            var redirectResult = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Index", redirectResult.ActionName);
            var error = controller.TempData["ErrorMessage"]?.ToString();
            Assert.NotNull(error);
            Assert.Contains("You cannot delete your own administrator account", error);

            // Admin still exists in DB
            var adminInDb = await context.Users.FindAsync(1);
            Assert.NotNull(adminInDb);
        }

        [Fact]
        public async Task DeleteConfirmed_SoleAdministrator_IsRejectedWhenTargetedByAnother()
        {
            // Arrange: Caller is Admin 10, target is Admin 1 (the only other admin, so adminCount = 1 for target role check)
            var (context, controller) = await CreateTestSetupAsync(currentAdminId: 10);

            // Target Admin 1 has Role Admin
            var result = await controller.DeleteConfirmed(1);

            // Assert: Deletion is rejected because adminCount <= 1
            var redirectResult = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Index", redirectResult.ActionName);
            var error = controller.TempData["ErrorMessage"]?.ToString();
            Assert.NotNull(error);
            Assert.Contains("Cannot delete the only Administrator in the system", error);
        }

        [Fact]
        public async Task Edit_SoleAdministrator_DemotionToReceptionist_IsRejected()
        {
            // Arrange: Only 1 Admin in system
            var (context, controller) = await CreateTestSetupAsync(currentAdminId: 1);
            var adminUser = await context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == 1);

            var demoteEdit = new User
            {
                Id = 1,
                Username = adminUser!.Username,
                FullName = adminUser.FullName,
                RoleId = 4, // Receptionist role
                Category = "Receptionist",
                CreatedAt = adminUser.CreatedAt
            };

            // Act: Attempt to demote sole admin
            var result = await controller.Edit(1, demoteEdit);

            // Assert: Blocked by sole administrator guard
            Assert.IsType<ViewResult>(result);
            Assert.False(controller.ModelState.IsValid);
            Assert.True(controller.ModelState.ContainsKey("RoleId"));
            var error = controller.ModelState["RoleId"]!.Errors.First().ErrorMessage;
            Assert.Contains("this user is the only Administrator in the system", error);
        }

        [Fact]
        public async Task Edit_MultipleAdmins_SelfDemotion_IsRejected()
        {
            // Arrange: Add a second admin so sole admin check passes
            var (context, controller) = await CreateTestSetupAsync(currentAdminId: 1);

            var secondAdmin = new User
            {
                Id = 2,
                Username = "admin2",
                FullName = "Second Admin",
                RoleId = 1, // Admin role
                Category = "Administrator",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            context.Users.Add(secondAdmin);
            await context.SaveChangesAsync();

            var adminUser = await context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == 1);
            var demoteEdit = new User
            {
                Id = 1,
                Username = adminUser!.Username,
                FullName = adminUser.FullName,
                RoleId = 2, // Demote to Doctor
                Category = "Doctor",
                CreatedAt = adminUser.CreatedAt
            };

            // Act: Admin 1 attempts to demote their own account
            var result = await controller.Edit(1, demoteEdit);

            // Assert: Blocked by self-demotion guard
            Assert.IsType<ViewResult>(result);
            Assert.False(controller.ModelState.IsValid);
            Assert.True(controller.ModelState.ContainsKey("RoleId"));
            var error = controller.ModelState["RoleId"]!.Errors.First().ErrorMessage;
            Assert.Contains("You cannot demote your own administrator account", error);
        }

        [Fact]
        public async Task Create_DuplicateUsername_IsRejectedWithValidationError()
        {
            // Arrange: Seeded context has "dr_alice"
            var (context, controller) = await CreateTestSetupAsync();

            var duplicateUser = new User
            {
                Username = "dr_alice", // Already exists!
                FullName = "Dr. Alice Clone",
                Password = "SecretPassword123!",
                RoleId = 2,
                Category = "Doctor"
            };

            // Act: Attempt to create user with duplicate username
            var result = await controller.Create(duplicateUser);

            // Assert: Model validation error
            Assert.IsType<ViewResult>(result);
            Assert.False(controller.ModelState.IsValid);
            Assert.True(controller.ModelState.ContainsKey("Username"));
            var error = controller.ModelState["Username"]!.Errors.First().ErrorMessage;
            Assert.Contains("already in use by another staff member", error);
        }

        [Fact]
        public async Task DeleteConfirmed_NonAdminUserWithoutLinks_IsDeleted()
        {
            // Arrange: Seeded context has cashier1 (Id: 30) with no links
            var (context, controller) = await CreateTestSetupAsync();

            // Act: Admin deletes cashier1
            var result = await controller.DeleteConfirmed(30);

            // Assert: Successfully deleted
            var redirectResult = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Index", redirectResult.ActionName);
            Assert.Contains("was deleted", controller.TempData["SuccessMessage"]?.ToString() ?? "");

            var dbUser = await context.Users.FindAsync(30);
            Assert.Null(dbUser);
        }
    }
}
