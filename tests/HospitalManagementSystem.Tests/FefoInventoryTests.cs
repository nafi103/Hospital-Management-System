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

namespace HospitalManagementSystem.Tests
{
    public class FefoInventoryTests
    {
        private async Task<(ApplicationDbContext Context, PrescriptionsController Controller)> CreateTestSetupAsync()
        {
            var context = await TestDbContextFactory.CreateSeededDbContextAsync();
            var controller = new PrescriptionsController(context);
            ControllerTestHelper.SetupController(controller, TestPrincipalFactory.CreatePharmacist(pharmacistId: 35));
            return (context, controller);
        }

        [Fact]
        public async Task Dispense_BatchesExist_AllocatesFromEarliestExpiringBatchFirst()
        {
            // Arrange
            var (context, controller) = await CreateTestSetupAsync();

            var med = new Medicine
            {
                Name = "FEFO Test Amoxicillin",
                GenericName = "Amoxicillin",
                StockQuantity = 100,
                UnitPrice = 10.00m,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            context.Medicines.Add(med);
            await context.SaveChangesAsync();

            var now = DateTime.UtcNow;
            var batchEarly = new MedicineBatch
            {
                MedicineId = med.Id,
                BatchNumber = "BAT-EARLY-01",
                QuantityRemaining = 10,
                InitialQuantity = 10,
                ExpiryDate = now.AddDays(10), // Expires sooner
                CreatedAt = now,
                UpdatedAt = now
            };
            var batchLate = new MedicineBatch
            {
                MedicineId = med.Id,
                BatchNumber = "BAT-LATE-02",
                QuantityRemaining = 20,
                InitialQuantity = 20,
                ExpiryDate = now.AddDays(40), // Expires later
                CreatedAt = now,
                UpdatedAt = now
            };
            context.MedicineBatches.AddRange(batchEarly, batchLate);

            var prescription = new Prescription
            {
                PatientId = 100,
                DoctorId = 10,
                Status = PrescriptionStatus.PendingPharmacy,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                PrescriptionItems = new List<PrescriptionItem>
                {
                    new PrescriptionItem { MedicineId = med.Id, Quantity = 14, DoseMorning = 1, DurationDays = 7 }
                }
            };
            context.Prescriptions.Add(prescription);
            await context.SaveChangesAsync();

            // Act - Dispense 14 units: should take 10 from batchEarly, and 4 from batchLate
            var result = await controller.Dispense(prescription.Id);

            // Assert
            var redirectResult = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal(nameof(PrescriptionsController.Details), redirectResult.ActionName);
            Assert.Equal("Medicines dispensed successfully. Stock has been updated.", controller.TempData["SuccessMessage"]);

            var updatedEarly = await context.MedicineBatches.AsNoTracking().FirstAsync(b => b.Id == batchEarly.Id);
            var updatedLate = await context.MedicineBatches.AsNoTracking().FirstAsync(b => b.Id == batchLate.Id);
            var updatedMed = await context.Medicines.AsNoTracking().FirstAsync(m => m.Id == med.Id);
            var updatedPrescription = await context.Prescriptions.AsNoTracking().FirstAsync(p => p.Id == prescription.Id);

            Assert.Equal(0, updatedEarly.QuantityRemaining); // All 10 deducted
            Assert.Equal(16, updatedLate.QuantityRemaining); // 20 - 4 = 16
            Assert.Equal(86, updatedMed.StockQuantity);      // 100 - 14 = 86
            Assert.Equal(PrescriptionStatus.Dispensed, updatedPrescription.Status);
        }

        [Fact]
        public async Task Dispense_ExpiredBatchesExist_BlocksDispensingWhenNonExpiredStockIsInsufficient()
        {
            // Arrange
            var (context, controller) = await CreateTestSetupAsync();

            var med = new Medicine
            {
                Name = "FEFO Test Ceftriaxone",
                GenericName = "Ceftriaxone",
                StockQuantity = 50,
                UnitPrice = 25.00m,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            context.Medicines.Add(med);
            await context.SaveChangesAsync();

            var now = DateTime.UtcNow;
            var expiredBatch = new MedicineBatch
            {
                MedicineId = med.Id,
                BatchNumber = "BAT-EXPIRED-99",
                QuantityRemaining = 30, // 30 in expired batch
                InitialQuantity = 30,
                ExpiryDate = now.AddDays(-2), // Expired!
                CreatedAt = now,
                UpdatedAt = now
            };
            var validBatch = new MedicineBatch
            {
                MedicineId = med.Id,
                BatchNumber = "BAT-VALID-01",
                QuantityRemaining = 5,  // Only 5 in valid batch
                InitialQuantity = 5,
                ExpiryDate = now.AddDays(25),
                CreatedAt = now,
                UpdatedAt = now
            };
            context.MedicineBatches.AddRange(expiredBatch, validBatch);

            var prescription = new Prescription
            {
                PatientId = 100,
                DoctorId = 10,
                Status = PrescriptionStatus.PendingPharmacy,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                PrescriptionItems = new List<PrescriptionItem>
                {
                    new PrescriptionItem { MedicineId = med.Id, Quantity = 10, DoseMorning = 1, DurationDays = 5 } // Needs 10
                }
            };
            context.Prescriptions.Add(prescription);
            await context.SaveChangesAsync();

            // Act - Needs 10, but non-expired stock is only 5 (even though total batch sum is 35 and med stock is 50)
            var result = await controller.Dispense(prescription.Id);

            // Assert
            var redirectResult = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal(nameof(PrescriptionsController.Details), redirectResult.ActionName);
            var error = controller.TempData["ErrorMessage"]?.ToString();
            Assert.NotNull(error);
            Assert.Contains("FEFO Inventory Lock", error);
            Assert.Contains("Expired batches are blocked from dispensing", error);

            // Verify status and quantities were NOT changed
            var updatedPrescription = await context.Prescriptions.AsNoTracking().FirstAsync(p => p.Id == prescription.Id);
            var updatedExpired = await context.MedicineBatches.AsNoTracking().FirstAsync(b => b.Id == expiredBatch.Id);
            var updatedValid = await context.MedicineBatches.AsNoTracking().FirstAsync(b => b.Id == validBatch.Id);
            var updatedMed = await context.Medicines.AsNoTracking().FirstAsync(m => m.Id == med.Id);

            Assert.Equal(PrescriptionStatus.PendingPharmacy, updatedPrescription.Status);
            Assert.Equal(30, updatedExpired.QuantityRemaining);
            Assert.Equal(5, updatedValid.QuantityRemaining);
            Assert.Equal(50, updatedMed.StockQuantity);
        }

        [Fact]
        public void MedicineBatch_ComputedProperties_CorrectlyDetermineExpiryStatus()
        {
            var now = DateTime.UtcNow;

            var expired = new MedicineBatch
            {
                ExpiryDate = now.AddDays(-1),
                QuantityRemaining = 10
            };
            var nearExpiry = new MedicineBatch
            {
                ExpiryDate = now.AddDays(15),
                QuantityRemaining = 10
            };
            var fresh = new MedicineBatch
            {
                ExpiryDate = now.AddDays(90),
                QuantityRemaining = 10
            };

            Assert.True(expired.IsExpired);
            Assert.False(expired.IsNearExpiry);

            Assert.False(nearExpiry.IsExpired);
            Assert.True(nearExpiry.IsNearExpiry);

            Assert.False(fresh.IsExpired);
            Assert.False(fresh.IsNearExpiry);
        }
    }
}
