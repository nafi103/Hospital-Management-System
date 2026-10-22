using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HospitalManagementSystem.Controllers;
using HospitalManagementSystem.Models;
using HospitalManagementSystem.Models.ViewModels;
using HospitalManagementSystem.Services;
using HospitalManagementSystem.Tests.Fixtures;
using HospitalManagementSystem.Tests.Helpers;
using Xunit;

namespace HospitalManagementSystem.Tests
{
    public class OperationSchedulingTests
    {
        private async Task<(ApplicationDbContext Context, OperationsController Controller, DoctorScheduleService ScheduleService)> CreateTestSetupAsync()
        {
            var context = await TestDbContextFactory.CreateSeededDbContextAsync();
            var scheduleService = new DoctorScheduleService(context);
            var controller = new OperationsController(context, scheduleService);
            ControllerTestHelper.SetupController(controller, TestPrincipalFactory.CreateDoctor(doctorId: 10));
            return (context, controller, scheduleService);
        }

        [Fact]
        public async Task Create_ValidOperation_SchedulesOperationAndEarmarksRecoveryBed()
        {
            // Arrange
            var (context, controller, _) = await CreateTestSetupAsync();

            var patient = await context.Patients.FirstAsync();
            var doctor = await context.Users.FirstAsync(u => u.RoleId == 2);
            var recoveryBed = await context.Beds.FirstAsync(b => b.Category == BedCategory.Cabin);

            var opTime = DateTime.UtcNow.Date.AddDays(3).AddHours(10); // 3 days from now at 10:00 UTC

            var model = new OperationCreateViewModel
            {
                PatientId = patient.Id,
                SurgeonId = doctor.Id,
                OtRoom = "OT-1 (General Surgery)",
                OperationDatetime = opTime,
                RecoveryBedId = recoveryBed.Id,
                PreparationChecklist = "NPO Confirmed; Anesthesia Clearance Signed; Consent Verified"
            };

            // Act
            var result = await controller.Create(model);

            // Assert
            var redirectResult = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal(nameof(OperationsController.Index), redirectResult.ActionName);

            var createdOp = await context.Operations
                .Include(o => o.Patient)
                .Include(o => o.Surgeon)
                .FirstOrDefaultAsync(o => o.PatientId == patient.Id && o.SurgeonId == doctor.Id);

            Assert.NotNull(createdOp);
            Assert.Equal(OperationStatus.Scheduled, createdOp.Status);
            Assert.Equal("OT-1 (General Surgery)", createdOp.OtRoom);
            Assert.Contains(recoveryBed.BedNumber, createdOp.PreparationChecklist);

            // Verify Admission and BedTransfer were created to earmark the recovery bed
            var activeAdmission = await context.Admissions
                .Include(a => a.BedTransfers)
                .FirstOrDefaultAsync(a => a.PatientId == patient.Id && a.DischargeDate == null);

            Assert.NotNull(activeAdmission);
            Assert.Contains(activeAdmission.BedTransfers, bt => bt.BedId == recoveryBed.Id && bt.EndDate == null);
        }

        [Fact]
        public async Task Create_SurgeonConflict_RejectsWithValidationError()
        {
            // Arrange
            var (context, controller, _) = await CreateTestSetupAsync();

            var patient = await context.Patients.FirstAsync();
            var doctor = await context.Users.FirstAsync(u => u.RoleId == 2);
            var recoveryBed = await context.Beds.FirstAsync(b => b.Category == BedCategory.Cabin);

            var opTime = DateTime.UtcNow.Date.AddDays(4).AddHours(14); // 4 days from now at 14:00 UTC

            // Create an overlapping outpatient consultation appointment for the surgeon
            var conflictingAppt = new Appointment
            {
                PatientId = patient.Id,
                DoctorId = doctor.Id,
                AppointmentDatetime = opTime.AddMinutes(15),
                EndTime = opTime.AddMinutes(30),
                Status = AppointmentStatus.Scheduled,
                ReasonForVisit = "Outpatient Clinic Review",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            context.Appointments.Add(conflictingAppt);
            await context.SaveChangesAsync();

            var model = new OperationCreateViewModel
            {
                PatientId = patient.Id,
                SurgeonId = doctor.Id,
                OtRoom = "OT-1 (General Surgery)",
                OperationDatetime = opTime,
                RecoveryBedId = recoveryBed.Id,
                PreparationChecklist = "Test Checklist"
            };

            // Act
            var result = await controller.Create(model);

            // Assert
            var viewResult = Assert.IsType<ViewResult>(result);
            Assert.False(controller.ModelState.IsValid);
            Assert.True(controller.ModelState.ContainsKey("SurgeonId"));
            var error = controller.ModelState["SurgeonId"]!.Errors.First().ErrorMessage;
            Assert.Contains("conflicting outpatient appointment", error);

            // Confirm no operation was booked
            var opExists = await context.Operations.AnyAsync(o => o.SurgeonId == doctor.Id && o.OperationDatetime == opTime);
            Assert.False(opExists);
        }

        [Fact]
        public async Task Create_OtRoomConflict_RejectsWithValidationError()
        {
            // Arrange
            var (context, controller, _) = await CreateTestSetupAsync();

            var patient = await context.Patients.FirstAsync();
            var surgeons = await context.Users.Where(u => u.RoleId == 2).ToListAsync();
            var surgeon1 = surgeons[0];
            var surgeon2 = surgeons.Count > 1 ? surgeons[1] : surgeon1;
            var recoveryBed = await context.Beds.FirstAsync(b => b.Category == BedCategory.Cabin);

            var opTime = DateTime.UtcNow.Date.AddDays(5).AddHours(9); // 5 days from now at 09:00 UTC

            // Book OT-2 for surgeon1 from 09:00 to 11:00 UTC
            var existingOp = new Operation
            {
                PatientId = patient.Id,
                SurgeonId = surgeon1.Id,
                OtRoom = "OT-2 (Orthopedic & Trauma)",
                OperationDatetime = opTime,
                Status = OperationStatus.Scheduled,
                PreparationChecklist = "Existing Procedure",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            context.Operations.Add(existingOp);
            await context.SaveChangesAsync();

            // Attempt to book the SAME OT room (OT-2) for surgeon2 at 10:00 UTC (overlaps 09:00 - 11:00)
            var model = new OperationCreateViewModel
            {
                PatientId = patient.Id,
                SurgeonId = surgeon2.Id,
                OtRoom = "OT-2 (Orthopedic & Trauma)",
                OperationDatetime = opTime.AddHours(1),
                RecoveryBedId = recoveryBed.Id,
                PreparationChecklist = "Second Procedure Attempt"
            };

            // Act
            var result = await controller.Create(model);

            // Assert
            var viewResult = Assert.IsType<ViewResult>(result);
            Assert.False(controller.ModelState.IsValid);
            Assert.True(controller.ModelState.ContainsKey("OtRoom"));
            var error = controller.ModelState["OtRoom"]!.Errors.First().ErrorMessage;
            Assert.Contains("already booked for another surgical operation", error);
        }

        [Fact]
        public async Task Create_OccupiedRecoveryBed_RejectsWithValidationError()
        {
            // Arrange
            var (context, controller, _) = await CreateTestSetupAsync();

            var patient = await context.Patients.FirstAsync();
            var doctor = await context.Users.FirstAsync(u => u.RoleId == 2);
            var recoveryBed = await context.Beds.FirstAsync(b => b.Category == BedCategory.Cabin);

            // Mark the recovery bed as occupied by creating an active BedTransfer
            var activeAdmission = new Admission
            {
                PatientId = patient.Id,
                AdmittingDoctorId = doctor.Id,
                AdmissionDate = DateTime.UtcNow.AddDays(-1),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            context.Admissions.Add(activeAdmission);
            await context.SaveChangesAsync();

            var activeTransfer = new BedTransfer
            {
                AdmissionId = activeAdmission.Id,
                BedId = recoveryBed.Id,
                StartDate = DateTime.UtcNow.AddDays(-1),
                EndDate = null // Occupied!
            };
            context.BedTransfers.Add(activeTransfer);
            await context.SaveChangesAsync();

            var opTime = DateTime.UtcNow.Date.AddDays(6).AddHours(11);

            var model = new OperationCreateViewModel
            {
                PatientId = patient.Id,
                SurgeonId = doctor.Id,
                OtRoom = "OT-3 (Laparoscopic / Day Care)",
                OperationDatetime = opTime,
                RecoveryBedId = recoveryBed.Id,
                PreparationChecklist = "Test Bed Check"
            };

            // Act
            var result = await controller.Create(model);

            // Assert
            var viewResult = Assert.IsType<ViewResult>(result);
            Assert.False(controller.ModelState.IsValid);
            Assert.True(controller.ModelState.ContainsKey("RecoveryBedId"));
            var error = controller.ModelState["RecoveryBedId"]!.Errors.First().ErrorMessage;
            Assert.Contains("currently occupied", error);
        }

        [Fact]
        public async Task UpdateStatus_TransitionsOperationThroughClinicalWorkflow()
        {
            // Arrange
            var (context, controller, _) = await CreateTestSetupAsync();

            var patient = await context.Patients.FirstAsync();
            var doctor = await context.Users.FirstAsync(u => u.RoleId == 2);

            var op = new Operation
            {
                PatientId = patient.Id,
                SurgeonId = doctor.Id,
                OtRoom = "OT-1 (General Surgery)",
                OperationDatetime = DateTime.UtcNow.AddHours(1),
                Status = OperationStatus.Scheduled,
                PreparationChecklist = "Checklist Completed",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            context.Operations.Add(op);
            await context.SaveChangesAsync();

            // Act 1: Transition to InSurgery
            var result1 = await controller.UpdateStatus(op.Id, OperationStatus.InSurgery);
            var redirect1 = Assert.IsType<RedirectToActionResult>(result1);
            Assert.Equal(nameof(OperationsController.Details), redirect1.ActionName);

            var updated1 = await context.Operations.AsNoTracking().FirstAsync(o => o.Id == op.Id);
            Assert.Equal(OperationStatus.InSurgery, updated1.Status);

            // Act 2: Transition to Completed
            var result2 = await controller.UpdateStatus(op.Id, OperationStatus.Completed);
            var redirect2 = Assert.IsType<RedirectToActionResult>(result2);
            Assert.Equal(nameof(OperationsController.Details), redirect2.ActionName);

            var updated2 = await context.Operations.AsNoTracking().FirstAsync(o => o.Id == op.Id);
            Assert.Equal(OperationStatus.Completed, updated2.Status);
        }
    }
}
