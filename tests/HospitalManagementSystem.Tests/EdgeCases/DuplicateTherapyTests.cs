using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using HospitalManagementSystem.Controllers;
using HospitalManagementSystem.Models;
using HospitalManagementSystem.Tests.Fixtures;
using HospitalManagementSystem.Tests.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HospitalManagementSystem.Tests.EdgeCases
{
    public class DuplicateTherapyTests
    {
        [Fact]
        public async Task CheckSafety_DraftWithTwoSameGenerics_ReturnsDuplicateTherapyWarning()
        {
            var context = await TestDbContextFactory.CreateSeededDbContextAsync();
            var controller = new PrescriptionsController(context);
            ControllerTestHelper.SetupController(controller, TestPrincipalFactory.CreateDoctor(doctorId: 10));

            // Napa (Id: 1, Paracetamol) and Ace (Id: 2, Paracetamol)
            var itemsJson = JsonSerializer.Serialize(new[]
            {
                new { medicineId = 1, quantity = 10, doseUnit = "Tablet" },
                new { medicineId = 2, quantity = 10, doseUnit = "Tablet" }
            });

            var result = await controller.CheckSafety(100, itemsJson) as JsonResult;
            Assert.NotNull(result);

            var json = JsonSerializer.Serialize(result.Value);
            Assert.Contains("Duplicate Therapy", json);
            Assert.Contains("Paracetamol", json);
        }

        [Fact]
        public async Task Create_DuplicateTherapyWithoutOverrideReason_ReturnsValidationError()
        {
            var context = await TestDbContextFactory.CreateSeededDbContextAsync();
            var controller = new PrescriptionsController(context);
            ControllerTestHelper.SetupController(controller, TestPrincipalFactory.CreateDoctor(doctorId: 10));

            var prescription = new Prescription
            {
                PatientId = 100,
                DoctorId = 10,
                ChiefComplaints = "Fever and headache",
                Diagnosis = "Viral flu"
            };

            var items = new List<PrescriptionItem>
            {
                new() { MedicineId = 1, Quantity = 10, DoseUnit = DoseUnit.Tablet, DoseMorning = 1, DurationDays = 5 },
                new() { MedicineId = 2, Quantity = 10, DoseUnit = DoseUnit.Tablet, DoseMorning = 1, DurationDays = 5 }
            };

            var result = await controller.Create(prescription, items);

            Assert.False(controller.ModelState.IsValid);
            Assert.True(controller.ModelState.ContainsKey(string.Empty));
            var error = Assert.Single(controller.ModelState[string.Empty]!.Errors);
            Assert.Contains("Safety warnings were raised", error.ErrorMessage);
        }

        [Fact]
        public async Task Create_DuplicateTherapyWithOverrideReason_SavesPrescriptionAndLogsWarnings()
        {
            var context = await TestDbContextFactory.CreateSeededDbContextAsync();
            var controller = new PrescriptionsController(context);
            ControllerTestHelper.SetupController(controller, TestPrincipalFactory.CreateDoctor(doctorId: 10));

            var prescription = new Prescription
            {
                PatientId = 100,
                DoctorId = 10,
                ChiefComplaints = "High fever",
                Diagnosis = "Severe pyrexia",
                SafetyOverrideReason = "Clinical decision: alternating paracetamol preparations under close monitoring."
            };

            var items = new List<PrescriptionItem>
            {
                new() { MedicineId = 1, Quantity = 10, DoseUnit = DoseUnit.Tablet, DoseMorning = 1, DurationDays = 5 },
                new() { MedicineId = 2, Quantity = 10, DoseUnit = DoseUnit.Tablet, DoseMorning = 1, DurationDays = 5 }
            };

            var result = await controller.Create(prescription, items);
            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Index", redirect.ActionName);

            var saved = await context.Prescriptions
                .Include(p => p.PrescriptionItems)
                .OrderByDescending(p => p.Id)
                .FirstOrDefaultAsync();

            Assert.NotNull(saved);
            Assert.Equal(PrescriptionStatus.PendingPharmacy, saved.Status);
            Assert.NotNull(saved.SafetyWarningsJson);
            Assert.Contains("Duplicate Therapy", saved.SafetyWarningsJson);
            Assert.Equal("Clinical decision: alternating paracetamol preparations under close monitoring.", saved.SafetyOverrideReason);
        }

        [Fact]
        public async Task CheckSafety_ActiveDispensedMedicationWithinWindow_ReturnsDuplicateTherapyWarning()
        {
            var context = await TestDbContextFactory.CreateSeededDbContextAsync();

            // Seed an active dispensed prescription: dispensed 5 days ago, duration 14 days -> active for 9 more days
            var dispensedPrescription = new Prescription
            {
                Id = 601,
                PatientId = 100,
                DoctorId = 10,
                Status = PrescriptionStatus.Dispensed,
                CreatedAt = DateTime.UtcNow.AddDays(-5),
                DispensedAt = DateTime.UtcNow.AddDays(-5),
                PrescriptionItems = new List<PrescriptionItem>
                {
                    new()
                    {
                        MedicineId = 2, // Ace (Paracetamol)
                        Quantity = 14,
                        DurationDays = 14,
                        DoseUnit = DoseUnit.Tablet,
                        UnitPrice = 2.0m
                    }
                }
            };
            context.Prescriptions.Add(dispensedPrescription);
            await context.SaveChangesAsync();

            var controller = new PrescriptionsController(context);
            ControllerTestHelper.SetupController(controller, TestPrincipalFactory.CreateDoctor(doctorId: 10));

            // Prescribing Napa (Id: 1, Paracetamol)
            var itemsJson = JsonSerializer.Serialize(new[]
            {
                new { medicineId = 1, quantity = 10, doseUnit = "Tablet" }
            });

            var result = await controller.CheckSafety(100, itemsJson) as JsonResult;
            Assert.NotNull(result);

            var json = JsonSerializer.Serialize(result.Value);
            Assert.Contains("Duplicate Therapy", json);
            Assert.Contains("Prescription #601", json);
            Assert.Contains("Ace", json);
            Assert.Contains("Paracetamol", json);
        }

        [Fact]
        public async Task CheckSafety_ExpiredDispensedMedicationBeyondWindow_ReturnsNoDuplicateWarning()
        {
            var context = await TestDbContextFactory.CreateSeededDbContextAsync();

            // Dispensed 40 days ago, duration 7 days -> expired 33 days ago
            var expiredPrescription = new Prescription
            {
                Id = 602,
                PatientId = 100,
                DoctorId = 10,
                Status = PrescriptionStatus.Dispensed,
                CreatedAt = DateTime.UtcNow.AddDays(-40),
                DispensedAt = DateTime.UtcNow.AddDays(-40),
                PrescriptionItems = new List<PrescriptionItem>
                {
                    new()
                    {
                        MedicineId = 2, // Ace (Paracetamol)
                        Quantity = 14,
                        DurationDays = 7,
                        DoseUnit = DoseUnit.Tablet,
                        UnitPrice = 2.0m
                    }
                }
            };
            context.Prescriptions.Add(expiredPrescription);
            await context.SaveChangesAsync();

            var controller = new PrescriptionsController(context);
            ControllerTestHelper.SetupController(controller, TestPrincipalFactory.CreateDoctor(doctorId: 10));

            // Prescribing Napa (Id: 1, Paracetamol)
            var itemsJson = JsonSerializer.Serialize(new[]
            {
                new { medicineId = 1, quantity = 10, doseUnit = "Tablet" }
            });

            var result = await controller.CheckSafety(100, itemsJson) as JsonResult;
            Assert.NotNull(result);

            var json = JsonSerializer.Serialize(result.Value);
            Assert.DoesNotContain("Duplicate Therapy", json);
            Assert.DoesNotContain("Prescription #602", json);
        }

        [Fact]
        public async Task CheckSafety_PendingNonDispensedPrescription_DoesNotCountAsActiveMedication()
        {
            var context = await TestDbContextFactory.CreateSeededDbContextAsync();

            // Created 2 days ago, but NOT Dispensed (still PendingPharmacy)
            var pendingPrescription = new Prescription
            {
                Id = 603,
                PatientId = 100,
                DoctorId = 10,
                Status = PrescriptionStatus.PendingPharmacy,
                CreatedAt = DateTime.UtcNow.AddDays(-2),
                PrescriptionItems = new List<PrescriptionItem>
                {
                    new()
                    {
                        MedicineId = 2, // Ace (Paracetamol)
                        Quantity = 14,
                        DurationDays = 14,
                        DoseUnit = DoseUnit.Tablet,
                        UnitPrice = 2.0m
                    }
                }
            };
            context.Prescriptions.Add(pendingPrescription);
            await context.SaveChangesAsync();

            var controller = new PrescriptionsController(context);
            ControllerTestHelper.SetupController(controller, TestPrincipalFactory.CreateDoctor(doctorId: 10));

            // Prescribing Napa (Id: 1, Paracetamol)
            var itemsJson = JsonSerializer.Serialize(new[]
            {
                new { medicineId = 1, quantity = 10, doseUnit = "Tablet" }
            });

            var result = await controller.CheckSafety(100, itemsJson) as JsonResult;
            Assert.NotNull(result);

            var json = JsonSerializer.Serialize(result.Value);
            Assert.DoesNotContain("Duplicate Therapy", json);
            Assert.DoesNotContain("Prescription #603", json);
        }

        [Fact]
        public async Task CheckSafety_DefaultDurationDays30_ActiveAtDay20_ExpiredAtDay35()
        {
            var context = await TestDbContextFactory.CreateSeededDbContextAsync();

            // Prescription A: dispensed 20 days ago, DurationDays = null (default 30) -> Still Active!
            var prescriptionA = new Prescription
            {
                Id = 604,
                PatientId = 100,
                DoctorId = 10,
                Status = PrescriptionStatus.Dispensed,
                CreatedAt = DateTime.UtcNow.AddDays(-20),
                DispensedAt = DateTime.UtcNow.AddDays(-20),
                PrescriptionItems = new List<PrescriptionItem>
                {
                    new()
                    {
                        MedicineId = 2, // Ace (Paracetamol)
                        Quantity = 30,
                        DurationDays = null,
                        DoseUnit = DoseUnit.Tablet,
                        UnitPrice = 2.0m
                    }
                }
            };

            // Prescription B: dispensed 35 days ago, DurationDays = null (default 30) -> Expired!
            var prescriptionB = new Prescription
            {
                Id = 605,
                PatientId = 100,
                DoctorId = 10,
                Status = PrescriptionStatus.Dispensed,
                CreatedAt = DateTime.UtcNow.AddDays(-35),
                DispensedAt = DateTime.UtcNow.AddDays(-35),
                PrescriptionItems = new List<PrescriptionItem>
                {
                    new()
                    {
                        MedicineId = 3, // Seclo (Omeprazole)
                        Quantity = 30,
                        DurationDays = null,
                        DoseUnit = DoseUnit.Capsule,
                        UnitPrice = 5.0m
                    }
                }
            };

            context.Prescriptions.AddRange(prescriptionA, prescriptionB);
            await context.SaveChangesAsync();

            var controller = new PrescriptionsController(context);
            ControllerTestHelper.SetupController(controller, TestPrincipalFactory.CreateDoctor(doctorId: 10));

            // Check Napa (Paracetamol) -> Should conflict with #604
            var itemsJson1 = JsonSerializer.Serialize(new[]
            {
                new { medicineId = 1, quantity = 10, doseUnit = "Tablet" }
            });
            var result1 = await controller.CheckSafety(100, itemsJson1) as JsonResult;
            var json1 = JsonSerializer.Serialize(result1?.Value);
            Assert.Contains("Prescription #604", json1);
            Assert.Contains("Duplicate Therapy", json1);

            // Check Seclo (Omeprazole) -> Should NOT conflict with #605 because 35 days > 30-day default
            var itemsJson2 = JsonSerializer.Serialize(new[]
            {
                new { medicineId = 3, quantity = 10, doseUnit = "Capsule" }
            });
            var result2 = await controller.CheckSafety(100, itemsJson2) as JsonResult;
            var json2 = JsonSerializer.Serialize(result2?.Value);
            Assert.DoesNotContain("Prescription #605", json2);
        }

        [Fact]
        public async Task CheckSafety_DifferentGenericDrug_ReturnsNoDuplicateWarning()
        {
            var context = await TestDbContextFactory.CreateSeededDbContextAsync();

            var activePrescription = new Prescription
            {
                Id = 606,
                PatientId = 100,
                DoctorId = 10,
                Status = PrescriptionStatus.Dispensed,
                CreatedAt = DateTime.UtcNow.AddDays(-3),
                DispensedAt = DateTime.UtcNow.AddDays(-3),
                PrescriptionItems = new List<PrescriptionItem>
                {
                    new()
                    {
                        MedicineId = 2, // Ace (Paracetamol)
                        Quantity = 10,
                        DurationDays = 7,
                        DoseUnit = DoseUnit.Tablet,
                        UnitPrice = 2.0m
                    }
                }
            };
            context.Prescriptions.Add(activePrescription);
            await context.SaveChangesAsync();

            var controller = new PrescriptionsController(context);
            ControllerTestHelper.SetupController(controller, TestPrincipalFactory.CreateDoctor(doctorId: 10));

            // Prescribe Omeprazole (Medicine 3)
            var itemsJson = JsonSerializer.Serialize(new[]
            {
                new { medicineId = 3, quantity = 14, doseUnit = "Capsule" }
            });

            var result = await controller.CheckSafety(100, itemsJson) as JsonResult;
            var json = JsonSerializer.Serialize(result?.Value);
            Assert.DoesNotContain("Duplicate Therapy", json);
        }
    }
}
