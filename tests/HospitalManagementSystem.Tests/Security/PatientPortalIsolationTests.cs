using System;
using System.Collections.Generic;
using System.Linq;
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
    public class PatientPortalIsolationTests
    {
        private async Task<(ApplicationDbContext Context, Patient PatientA, Patient PatientB)> SetupTwoPatientsAsync()
        {
            var context = await TestDbContextFactory.CreateSeededDbContextAsync();

            // Link patient 100 to user 1000
            var patientA = await context.Patients.FindAsync(100);
            patientA!.UserId = 1000;

            // Link patient 101 to user 1001
            var patientB = await context.Patients.FindAsync(101);
            patientB!.UserId = 1001;

            await context.SaveChangesAsync();
            return (context, patientA, patientB);
        }

        [Fact]
        public async Task Portal_Index_FiltersStrictlyToLoggedInPatient()
        {
            // Arrange
            var (context, patientA, patientB) = await SetupTwoPatientsAsync();

            var apptA = new Appointment
            {
                Id = 301,
                PatientId = patientA.Id,
                DoctorId = 10,
                AppointmentDatetime = DateTime.UtcNow.AddDays(1),
                EndTime = DateTime.UtcNow.AddDays(1).AddMinutes(15),
                Status = AppointmentStatus.Scheduled,
                ReasonForVisit = "Patient A Visit"
            };

            var apptB = new Appointment
            {
                Id = 302,
                PatientId = patientB.Id,
                DoctorId = 10,
                AppointmentDatetime = DateTime.UtcNow.AddDays(1),
                EndTime = DateTime.UtcNow.AddDays(1).AddMinutes(15),
                Status = AppointmentStatus.Scheduled,
                ReasonForVisit = "Patient B Visit"
            };

            context.Appointments.AddRange(apptA, apptB);
            await context.SaveChangesAsync();

            var controller = new PortalController(context, new DoctorScheduleService(context));
            ControllerTestHelper.SetupController(controller, TestPrincipalFactory.CreatePatient(patientUserId: 1000, uhid: patientA.Uhid));

            // Act: Patient A visits portal
            var result = await controller.Index();

            // Assert: View rendered, and only Patient A's appointment is present
            Assert.IsType<ViewResult>(result);
            var upcoming = Assert.IsAssignableFrom<List<Appointment>>(controller.ViewBag.Upcoming);
            Assert.Single(upcoming);
            Assert.Equal(301, upcoming[0].Id);
            Assert.Equal(patientA.Id, upcoming[0].PatientId);
        }

        [Fact]
        public async Task Portal_Records_OnlyReturnsOwnMedicalRecords()
        {
            // Arrange
            var (context, patientA, patientB) = await SetupTwoPatientsAsync();

            var recA = new MedicalRecord
            {
                PatientId = patientA.Id,
                DoctorId = 10,
                Diagnosis = "Flu A",
                Treatment = "Rest",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            var recB = new MedicalRecord
            {
                PatientId = patientB.Id,
                DoctorId = 10,
                Diagnosis = "Flu B",
                Treatment = "Rest",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            context.MedicalRecords.AddRange(recA, recB);
            await context.SaveChangesAsync();

            var controller = new PortalController(context, new DoctorScheduleService(context));
            ControllerTestHelper.SetupController(controller, TestPrincipalFactory.CreatePatient(patientUserId: 1000, uhid: patientA.Uhid));

            // Act
            var result = await controller.Records();

            // Assert
            var viewResult = Assert.IsType<ViewResult>(result);
            var records = Assert.IsAssignableFrom<List<MedicalRecord>>(viewResult.Model);
            Assert.Single(records);
            Assert.Equal("Flu A", records[0].Diagnosis);
            Assert.Equal(patientA.Id, records[0].PatientId);
        }

        [Fact]
        public async Task Portal_Bills_OnlyReturnsOwnBills()
        {
            // Arrange
            var (context, patientA, patientB) = await SetupTwoPatientsAsync();

            var billA = new Bill
            {
                PatientId = patientA.Id,
                SubtotalAmount = 500m,
                NetTotal = 500m,
                Status = BillStatus.Unpaid,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            var billB = new Bill
            {
                PatientId = patientB.Id,
                SubtotalAmount = 1200m,
                NetTotal = 1200m,
                Status = BillStatus.Unpaid,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            context.Bills.AddRange(billA, billB);
            await context.SaveChangesAsync();

            var controller = new PortalController(context, new DoctorScheduleService(context));
            ControllerTestHelper.SetupController(controller, TestPrincipalFactory.CreatePatient(patientUserId: 1000, uhid: patientA.Uhid));

            // Act
            var result = await controller.Bills();

            // Assert
            var viewResult = Assert.IsType<ViewResult>(result);
            var bills = Assert.IsAssignableFrom<List<Bill>>(viewResult.Model);
            Assert.Single(bills);
            Assert.Equal(500m, bills[0].NetTotal);
            Assert.Equal(patientA.Id, bills[0].PatientId);
        }

        [Fact]
        public async Task Prescriptions_Print_CrossPatientAttempt_ReturnsForbid()
        {
            // Arrange
            var (context, patientA, patientB) = await SetupTwoPatientsAsync();

            var presB = new Prescription
            {
                Id = 501,
                PatientId = patientB.Id,
                DoctorId = 10,
                Status = PrescriptionStatus.Dispensed,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                PrescriptionItems = new List<PrescriptionItem>
                {
                    new PrescriptionItem { MedicineId = 1, Quantity = 5, DoseMorning = 1, DurationDays = 5 }
                }
            };
            context.Prescriptions.Add(presB);
            await context.SaveChangesAsync();

            var controller = new PrescriptionsController(context);
            // Patient A is logged in
            ControllerTestHelper.SetupController(controller, TestPrincipalFactory.CreatePatient(patientUserId: 1000, uhid: patientA.Uhid));

            // Act: Patient A attempts to print Patient B's prescription
            var result = await controller.Print(501);

            // Assert: Access is strictly forbidden
            Assert.IsType<ForbidResult>(result);
        }

        [Fact]
        public async Task Prescriptions_Print_OwnPrescription_Allowed()
        {
            // Arrange
            var (context, patientA, _) = await SetupTwoPatientsAsync();

            var presA = new Prescription
            {
                Id = 502,
                PatientId = patientA.Id,
                DoctorId = 10,
                Status = PrescriptionStatus.Dispensed,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                PrescriptionItems = new List<PrescriptionItem>
                {
                    new PrescriptionItem { MedicineId = 1, Quantity = 5, DoseMorning = 1, DurationDays = 5 }
                }
            };
            context.Prescriptions.Add(presA);
            await context.SaveChangesAsync();

            var controller = new PrescriptionsController(context);
            ControllerTestHelper.SetupController(controller, TestPrincipalFactory.CreatePatient(patientUserId: 1000, uhid: patientA.Uhid));

            // Act: Patient A prints own prescription
            var result = await controller.Print(502);

            // Assert: Allowed
            var viewResult = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<Prescription>(viewResult.Model);
            Assert.Equal(502, model.Id);
            Assert.Equal(patientA.Id, model.PatientId);
        }

        [Fact]
        public async Task Portal_UnlinkedPatientUser_ReturnsForbid()
        {
            // Arrange: Seeded context, user has role "Patient" but no matching Patient.UserId
            var context = await TestDbContextFactory.CreateSeededDbContextAsync();
            var controller = new PortalController(context, new DoctorScheduleService(context));
            ControllerTestHelper.SetupController(controller, TestPrincipalFactory.CreatePatient(patientUserId: 9999, uhid: "PT-UNKNOWN"));

            // Act
            var result = await controller.Index();

            // Assert: Cannot access portal without linked Patient record
            Assert.IsType<ForbidResult>(result);
        }
    }
}
