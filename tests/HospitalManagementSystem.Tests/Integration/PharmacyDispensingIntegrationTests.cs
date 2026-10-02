using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HospitalManagementSystem.Controllers;
using HospitalManagementSystem.Models;
using HospitalManagementSystem.Tests.Fixtures;
using HospitalManagementSystem.Tests.Helpers;
using Xunit;

namespace HospitalManagementSystem.Tests.Integration
{
    public class PharmacyDispensingIntegrationTests
    {
        private async Task<(ApplicationDbContext Context, PrescriptionsController Controller)> CreateTestSetupAsync()
        {
            var context = await TestDbContextFactory.CreateSeededDbContextAsync();
            var controller = new PrescriptionsController(context);
            ControllerTestHelper.SetupController(controller, TestPrincipalFactory.CreatePharmacist(pharmacistId: 35));
            return (context, controller);
        }

        [Fact]
        public async Task Dispense_ValidPrescription_DeductsStockAtomicallyAndMarksDispensed()
        {
            // Arrange
            var (context, controller) = await CreateTestSetupAsync();

            var prescription = new Prescription
            {
                PatientId = 100,
                DoctorId = 10,
                Status = PrescriptionStatus.PendingPharmacy,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                PrescriptionItems = new List<PrescriptionItem>
                {
                    new PrescriptionItem { MedicineId = 1, Quantity = 10, DoseMorning = 1, DoseEvening = 1, DurationDays = 5 }, // Napa (Stock: 100)
                    new PrescriptionItem { MedicineId = 2, Quantity = 5, DoseMorning = 1, DurationDays = 5 }   // Ace (Stock: 50)
                }
            };
            context.Prescriptions.Add(prescription);
            await context.SaveChangesAsync();

            // Act
            var result = await controller.Dispense(prescription.Id);

            // Assert
            var redirectResult = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal(nameof(PrescriptionsController.Details), redirectResult.ActionName);
            Assert.Equal("Medicines dispensed successfully. Stock has been updated.", controller.TempData["SuccessMessage"]);

            // Reload from context
            var updatedPrescription = await context.Prescriptions.AsNoTracking().FirstOrDefaultAsync(p => p.Id == prescription.Id);
            Assert.NotNull(updatedPrescription);
            Assert.Equal(PrescriptionStatus.Dispensed, updatedPrescription.Status);
            Assert.Equal(35, updatedPrescription.DispensedById);
            Assert.NotNull(updatedPrescription.DispensedAt);

            var med1 = await context.Medicines.AsNoTracking().FirstOrDefaultAsync(m => m.Id == 1);
            var med2 = await context.Medicines.AsNoTracking().FirstOrDefaultAsync(m => m.Id == 2);
            Assert.Equal(90, med1!.StockQuantity); // 100 - 10
            Assert.Equal(45, med2!.StockQuantity); // 50 - 5
        }

        [Fact]
        public async Task Dispense_InsufficientStock_RollsBackEntireTransactionAndLeavesPrescriptionPending()
        {
            // Arrange: Item 1 has sufficient stock, but Item 2 exceeds available stock
            var (context, controller) = await CreateTestSetupAsync();

            var prescription = new Prescription
            {
                PatientId = 100,
                DoctorId = 10,
                Status = PrescriptionStatus.PendingPharmacy,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                PrescriptionItems = new List<PrescriptionItem>
                {
                    new PrescriptionItem { MedicineId = 1, Quantity = 10, DoseMorning = 1, DoseEvening = 1, DurationDays = 5 },  // Napa (Stock: 100)
                    new PrescriptionItem { MedicineId = 2, Quantity = 999, DoseMorning = 1, DurationDays = 30 } // Ace (Stock: only 50!)
                }
            };
            context.Prescriptions.Add(prescription);
            await context.SaveChangesAsync();

            // Act
            var result = await controller.Dispense(prescription.Id);

            // Assert
            var redirectResult = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal(nameof(PrescriptionsController.Details), redirectResult.ActionName);
            var error = controller.TempData["ErrorMessage"]?.ToString();
            Assert.NotNull(error);
            Assert.Contains("Insufficient stock", error);

            // Status MUST remain PendingPharmacy
            var dbPrescription = await context.Prescriptions.AsNoTracking().FirstOrDefaultAsync(p => p.Id == prescription.Id);
            Assert.NotNull(dbPrescription);
            Assert.Equal(PrescriptionStatus.PendingPharmacy, dbPrescription.Status);
            Assert.Null(dbPrescription.DispensedAt);

            // Both stocks must be completely unchanged due to rollback
            var med1 = await context.Medicines.AsNoTracking().FirstOrDefaultAsync(m => m.Id == 1);
            var med2 = await context.Medicines.AsNoTracking().FirstOrDefaultAsync(m => m.Id == 2);
            Assert.Equal(100, med1!.StockQuantity);
            Assert.Equal(50, med2!.StockQuantity);
        }

        [Fact]
        public async Task Dispense_AlreadyDispensedPrescription_IsRejected()
        {
            // Arrange: Prescription already Dispensed
            var (context, controller) = await CreateTestSetupAsync();

            var prescription = new Prescription
            {
                PatientId = 100,
                DoctorId = 10,
                Status = PrescriptionStatus.Dispensed,
                DispensedById = 35,
                DispensedAt = DateTime.UtcNow.AddHours(-1),
                CreatedAt = DateTime.UtcNow.AddHours(-2),
                UpdatedAt = DateTime.UtcNow.AddHours(-1),
                PrescriptionItems = new List<PrescriptionItem>
                {
                    new PrescriptionItem { MedicineId = 1, Quantity = 5, DoseMorning = 1, DurationDays = 5 }
                }
            };
            context.Prescriptions.Add(prescription);
            await context.SaveChangesAsync();

            // Act: Attempt to dispense again
            var result = await controller.Dispense(prescription.Id);

            // Assert
            var redirectResult = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal(nameof(PrescriptionsController.Details), redirectResult.ActionName);
            Assert.Equal("This prescription has already been dispensed.", controller.TempData["ErrorMessage"]);

            // Stock unchanged
            var med1 = await context.Medicines.AsNoTracking().FirstOrDefaultAsync(m => m.Id == 1);
            Assert.Equal(100, med1!.StockQuantity);
        }

        [Fact]
        public async Task Dispense_NonExistentPrescription_ReturnsNotFound()
        {
            // Arrange
            var (context, controller) = await CreateTestSetupAsync();

            // Act
            var result = await controller.Dispense(99999);

            // Assert
            Assert.IsType<NotFoundResult>(result);
        }
    }
}
