using System;
using System.Collections.Generic;
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
    public class ReferentialIntegrityTests
    {
        [Fact]
        public async Task DeleteMedicine_ReferencedInPrescriptionItems_IsRejectedWithWarning()
        {
            // Arrange: Seeded medicine 1 (Napa) referenced in a prescription
            var context = await TestDbContextFactory.CreateSeededDbContextAsync();
            var prescription = new Prescription
            {
                PatientId = 100,
                DoctorId = 10,
                Status = PrescriptionStatus.PendingPharmacy,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                PrescriptionItems = new List<PrescriptionItem>
                {
                    new PrescriptionItem { MedicineId = 1, Quantity = 10, DoseMorning = 1, DurationDays = 5 }
                }
            };
            context.Prescriptions.Add(prescription);
            await context.SaveChangesAsync();

            var controller = new MedicinesController(context);
            ControllerTestHelper.SetupController(controller, TestPrincipalFactory.CreateAdmin());

            // Act: Attempt to delete medicine 1
            var result = await controller.DeleteConfirmed(1);

            // Assert: Deletion is rejected
            var redirectResult = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Index", redirectResult.ActionName);
            var error = controller.TempData["ErrorMessage"]?.ToString();
            Assert.NotNull(error);
            Assert.Contains("Cannot delete this medicine because it is referenced in historical patient prescriptions", error);

            // Medicine still exists in database
            var med = await context.Medicines.FindAsync(1);
            Assert.NotNull(med);
        }

        [Fact]
        public async Task DeleteMedicine_Unreferenced_IsDeletedSuccessfully()
        {
            // Arrange: Add a standalone unreferenced medicine
            var context = await TestDbContextFactory.CreateSeededDbContextAsync();
            var unreferencedMed = new Medicine
            {
                Id = 99,
                Name = "Temporary Vitamin",
                GenericName = "Multivitamin",
                Strength = "10mg",
                StockQuantity = 10,
                UnitPrice = 1.00m,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            context.Medicines.Add(unreferencedMed);
            await context.SaveChangesAsync();

            var controller = new MedicinesController(context);
            ControllerTestHelper.SetupController(controller, TestPrincipalFactory.CreateAdmin());

            // Act: Delete unreferenced medicine
            var result = await controller.DeleteConfirmed(99);

            // Assert: Deletion succeeds
            var redirectResult = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Index", redirectResult.ActionName);
            Assert.Equal("Medicine deleted successfully.", controller.TempData["SuccessMessage"]);

            // Medicine no longer in database
            var med = await context.Medicines.FindAsync(99);
            Assert.Null(med);
        }

        [Fact]
        public async Task DeleteAdmission_WithAttachedBills_IsBlocked()
        {
            // Arrange: Admission with an existing Bill
            var context = await TestDbContextFactory.CreateSeededDbContextAsync();
            var admission = new Admission
            {
                Id = 50,
                PatientId = 100,
                AdmittingDoctorId = 10,
                AdmissionDate = DateTime.UtcNow.AddDays(-3),
                CreatedAt = DateTime.UtcNow.AddDays(-3),
                UpdatedAt = DateTime.UtcNow.AddDays(-3)
            };
            context.Admissions.Add(admission);

            var bill = new Bill
            {
                Id = 200,
                PatientId = 100,
                AdmissionId = 50,
                SubtotalAmount = 1500m,
                NetTotal = 1500m,
                PaidAmount = 0m,
                Status = BillStatus.Unpaid,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            context.Bills.Add(bill);
            await context.SaveChangesAsync();

            var controller = new AdmissionsController(context);
            ControllerTestHelper.SetupController(controller, TestPrincipalFactory.CreateAdmin());

            // Act: Attempt to delete admission with attached bill
            var result = await controller.DeleteConfirmed(50);

            // Assert: Blocked with ErrorMessage
            var redirectResult = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Index", redirectResult.ActionName);
            var error = controller.TempData["ErrorMessage"]?.ToString();
            Assert.NotNull(error);
            Assert.Contains("Cannot delete this admission because billing records are attached", error);

            // Admission still exists
            var dbAdmission = await context.Admissions.FindAsync(50);
            Assert.NotNull(dbAdmission);
        }

        [Fact]
        public async Task TransferBed_AlreadyDischargedAdmission_IsRejected()
        {
            // Arrange: Admission that is already discharged
            var context = await TestDbContextFactory.CreateSeededDbContextAsync();
            var dischargedAdmission = new Admission
            {
                Id = 60,
                PatientId = 100,
                AdmittingDoctorId = 10,
                AdmissionDate = DateTime.UtcNow.AddDays(-5),
                DischargeDate = DateTime.UtcNow.AddDays(-1), // Discharged!
                CreatedAt = DateTime.UtcNow.AddDays(-5),
                UpdatedAt = DateTime.UtcNow.AddDays(-1)
            };
            context.Admissions.Add(dischargedAdmission);
            await context.SaveChangesAsync();

            var controller = new AdmissionsController(context);
            ControllerTestHelper.SetupController(controller, TestPrincipalFactory.CreateAdmin());

            // Act: Attempt to transfer bed for discharged patient
            var result = await controller.TransferBed(60, 2);

            // Assert: Rejected
            var redirectResult = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Index", redirectResult.ActionName);
            var error = controller.TempData["ErrorMessage"]?.ToString();
            Assert.NotNull(error);
            Assert.Contains("Cannot transfer beds for a patient who has already been discharged", error);
        }

        [Fact]
        public async Task TransferBed_TargetBedOccupied_IsRejected()
        {
            // Arrange: Bed 1 is occupied by Admission 70
            var context = await TestDbContextFactory.CreateSeededDbContextAsync();
            var admissionActive = new Admission
            {
                Id = 70,
                PatientId = 100,
                AdmittingDoctorId = 10,
                AdmissionDate = DateTime.UtcNow.AddDays(-2),
                CreatedAt = DateTime.UtcNow.AddDays(-2),
                UpdatedAt = DateTime.UtcNow.AddDays(-2),
                BedTransfers = new List<BedTransfer>
                {
                    new BedTransfer { BedId = 1, StartDate = DateTime.UtcNow.AddDays(-2), EndDate = null }
                }
            };

            // Admission 71 currently in Bed 2, wants to transfer to Bed 1 (which is occupied)
            var admissionTransferring = new Admission
            {
                Id = 71,
                PatientId = 101,
                AdmittingDoctorId = 10,
                AdmissionDate = DateTime.UtcNow.AddDays(-1),
                CreatedAt = DateTime.UtcNow.AddDays(-1),
                UpdatedAt = DateTime.UtcNow.AddDays(-1),
                BedTransfers = new List<BedTransfer>
                {
                    new BedTransfer { BedId = 2, StartDate = DateTime.UtcNow.AddDays(-1), EndDate = null }
                }
            };

            context.Admissions.AddRange(admissionActive, admissionTransferring);
            await context.SaveChangesAsync();

            var controller = new AdmissionsController(context);
            ControllerTestHelper.SetupController(controller, TestPrincipalFactory.CreateAdmin());

            // Act: Attempt transfer to Bed 1 (occupied)
            var result = await controller.TransferBed(71, 1);

            // Assert: Transfer rejected due to occupied bed
            var redirectResult = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("TransferBed", redirectResult.ActionName);
            var error = controller.TempData["ErrorMessage"]?.ToString();
            Assert.NotNull(error);
            Assert.Contains("The target bed is currently occupied", error);
        }
    }
}
