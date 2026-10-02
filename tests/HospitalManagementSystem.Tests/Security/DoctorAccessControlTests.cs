using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HospitalManagementSystem.Controllers;
using HospitalManagementSystem.Hubs;
using HospitalManagementSystem.Models;
using HospitalManagementSystem.Tests.Fixtures;
using HospitalManagementSystem.Tests.Helpers;
using Xunit;

namespace HospitalManagementSystem.Tests.Security
{
    public class DoctorAccessControlTests
    {
        [Fact]
        public async Task MedicalRecords_Details_DoctorWithoutCareRelationship_ReturnsForbid()
        {
            // Arrange: Dr Alice (Id: 10) creates a record for Patient 100
            var context = await TestDbContextFactory.CreateSeededDbContextAsync();
            var record = new MedicalRecord
            {
                Id = 1,
                PatientId = 100,
                DoctorId = 10,
                Diagnosis = "Acute Bronchitis",
                Treatment = "Treatment",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            context.MedicalRecords.Add(record);
            await context.SaveChangesAsync();

            // Act: Dr Bob (Id: 11) attempts to view Patient 100's record with NO care relationship
            var controller = new MedicalRecordsController(context);
            ControllerTestHelper.SetupController(controller, TestPrincipalFactory.CreateDoctor(doctorId: 11, username: "dr_bob"));

            var result = await controller.Details(1);

            // Assert: Cross-doctor snooping is strictly blocked
            Assert.IsType<ForbidResult>(result);
        }

        [Fact]
        public async Task MedicalRecords_Details_DoctorWithActiveAppointment_Allowed()
        {
            // Arrange: Record created by Dr Alice, but Dr Bob has an active appointment with the patient
            var context = await TestDbContextFactory.CreateSeededDbContextAsync();
            var record = new MedicalRecord
            {
                Id = 1,
                PatientId = 100,
                DoctorId = 10,
                Diagnosis = "Hypertension",
                Treatment = "Lifestyle modifications",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            var bobsAppointment = new Appointment
            {
                PatientId = 100,
                DoctorId = 11, // Dr Bob
                AppointmentDatetime = DateTime.UtcNow.AddHours(2),
                EndTime = DateTime.UtcNow.AddHours(2).AddMinutes(15),
                Status = AppointmentStatus.Scheduled,
                ReasonForVisit = "Cardio consult",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            context.MedicalRecords.Add(record);
            context.Appointments.Add(bobsAppointment);
            await context.SaveChangesAsync();

            // Act: Dr Bob views the record
            var controller = new MedicalRecordsController(context);
            ControllerTestHelper.SetupController(controller, TestPrincipalFactory.CreateDoctor(doctorId: 11, username: "dr_bob"));

            var result = await controller.Details(1);

            // Assert: Care relationship verified, access granted
            var viewResult = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<MedicalRecord>(viewResult.Model);
            Assert.Equal("Hypertension", model.Diagnosis);
        }

        [Fact]
        public async Task MedicalRecords_Details_DoctorWithAdmission_Allowed()
        {
            // Arrange: Record created by Dr Alice, but Dr Bob is the admitting doctor
            var context = await TestDbContextFactory.CreateSeededDbContextAsync();
            var record = new MedicalRecord
            {
                Id = 1,
                PatientId = 100,
                DoctorId = 10,
                Diagnosis = "Appendicitis",
                Treatment = "Surgical consult",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            var bobsAdmission = new Admission
            {
                PatientId = 100,
                AdmittingDoctorId = 11, // Dr Bob
                AdmissionDate = DateTime.UtcNow.AddDays(-1),
                CreatedAt = DateTime.UtcNow.AddDays(-1),
                UpdatedAt = DateTime.UtcNow.AddDays(-1)
            };
            context.MedicalRecords.Add(record);
            context.Admissions.Add(bobsAdmission);
            await context.SaveChangesAsync();

            // Act: Dr Bob views the record
            var controller = new MedicalRecordsController(context);
            ControllerTestHelper.SetupController(controller, TestPrincipalFactory.CreateDoctor(doctorId: 11, username: "dr_bob"));

            var result = await controller.Details(1);

            // Assert: Access granted through admission care relationship
            var viewResult = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<MedicalRecord>(viewResult.Model);
            Assert.Equal("Appendicitis", model.Diagnosis);
        }

        [Fact]
        public async Task MedicalRecords_Details_AuthorDoctor_Allowed()
        {
            // Arrange: Dr Alice views her own record
            var context = await TestDbContextFactory.CreateSeededDbContextAsync();
            var record = new MedicalRecord
            {
                Id = 1,
                PatientId = 100,
                DoctorId = 10,
                Diagnosis = "Seasonal Allergies",
                Treatment = "Antihistamines",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            context.MedicalRecords.Add(record);
            await context.SaveChangesAsync();

            var controller = new MedicalRecordsController(context);
            ControllerTestHelper.SetupController(controller, TestPrincipalFactory.CreateDoctor(doctorId: 10, username: "dr_alice"));

            var result = await controller.Details(1);

            var viewResult = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<MedicalRecord>(viewResult.Model);
            Assert.Equal("Seasonal Allergies", model.Diagnosis);
        }

        [Fact]
        public async Task DoctorDashboard_Index_OnlyShowsOwnConsultations()
        {
            // Arrange: Setup 2 active consultations
            var context = await TestDbContextFactory.CreateSeededDbContextAsync();
            var aliceAppt = new Appointment
            {
                Id = 101,
                PatientId = 100,
                DoctorId = 10, // Dr Alice
                Status = AppointmentStatus.InConsultation,
                AppointmentDatetime = DateTime.UtcNow.AddHours(-1),
                EndTime = DateTime.UtcNow.AddHours(-1).AddMinutes(15),
                ReasonForVisit = "Alice's Queue Patient",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            var bobAppt = new Appointment
            {
                Id = 102,
                PatientId = 101,
                DoctorId = 11, // Dr Bob
                Status = AppointmentStatus.InConsultation,
                AppointmentDatetime = DateTime.UtcNow.AddHours(-1),
                EndTime = DateTime.UtcNow.AddHours(-1).AddMinutes(15),
                ReasonForVisit = "Bob's Queue Patient",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            context.Appointments.AddRange(aliceAppt, bobAppt);
            await context.SaveChangesAsync();

            var controller = new DoctorDashboardController(context, new TestHubContext<NotificationHub>());
            ControllerTestHelper.SetupController(controller, TestPrincipalFactory.CreateDoctor(doctorId: 10, username: "dr_alice"));

            // Act: Dr Alice visits dashboard
            var result = await controller.Index();

            // Assert: Dr Alice sees only her own appointment
            var viewResult = Assert.IsType<ViewResult>(result);
            var model = Assert.IsAssignableFrom<IEnumerable<Appointment>>(viewResult.Model);
            var appointments = model.ToList();
            Assert.Single(appointments);
            Assert.Equal(101, appointments[0].Id);
            Assert.Equal(10, appointments[0].DoctorId);
        }

        [Fact]
        public async Task DoctorDashboard_MarkCompleted_CrossDoctorAttempt_ReturnsForbid()
        {
            // Arrange: Dr Alice's appointment is InConsultation
            var context = await TestDbContextFactory.CreateSeededDbContextAsync();
            var aliceAppt = new Appointment
            {
                Id = 101,
                PatientId = 100,
                DoctorId = 10, // Dr Alice
                Status = AppointmentStatus.InConsultation,
                AppointmentDatetime = DateTime.UtcNow.AddHours(-1),
                EndTime = DateTime.UtcNow.AddHours(-1).AddMinutes(15),
                ReasonForVisit = "Consultation",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            context.Appointments.Add(aliceAppt);
            await context.SaveChangesAsync();

            // Act: Dr Bob (Id: 11) attempts to mark Dr Alice's appointment as completed
            var controller = new DoctorDashboardController(context, new TestHubContext<NotificationHub>());
            ControllerTestHelper.SetupController(controller, TestPrincipalFactory.CreateDoctor(doctorId: 11, username: "dr_bob"));

            var result = await controller.MarkCompleted(101);

            // Assert: Strictly forbidden
            Assert.IsType<ForbidResult>(result);

            // Verify status in DB remains InConsultation
            var dbAppt = await context.Appointments.FindAsync(101);
            Assert.Equal(AppointmentStatus.InConsultation, dbAppt!.Status);
        }
    }
}
