using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using HospitalManagementSystem.Controllers;
using HospitalManagementSystem.Hubs;
using HospitalManagementSystem.Models;
using HospitalManagementSystem.Tests.Fixtures;
using HospitalManagementSystem.Tests.Helpers;
using Xunit;

namespace HospitalManagementSystem.Tests.Security
{
    public class AssistantAccessControlTests
    {
        [Fact]
        public async Task Vitals_Create_Get_CrossChamberDoctor_ReturnsForbid()
        {
            // Arrange: Dr Bob (Id: 11) has an appointment
            var context = await TestDbContextFactory.CreateSeededDbContextAsync();
            var bobsAppointment = new Appointment
            {
                Id = 200,
                PatientId = 100,
                DoctorId = 11, // Dr Bob
                AppointmentDatetime = DateTime.UtcNow.AddHours(1),
                EndTime = DateTime.UtcNow.AddHours(1).AddMinutes(15),
                Status = AppointmentStatus.Scheduled,
                ReasonForVisit = "Dr Bob Appointment",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            context.Appointments.Add(bobsAppointment);
            await context.SaveChangesAsync();

            // Act: Assistant Anna (AssignedDoctorId: 10, Dr Alice) attempts to record vitals for Dr Bob's patient
            var controller = new VitalsController(context);
            ControllerTestHelper.SetupController(controller, TestPrincipalFactory.CreateAssistant(assistantId: 20, assignedDoctorId: 10));

            var result = await controller.Create(appointmentId: 200);

            // Assert: Assistant is strictly forbidden from accessing another doctor's chamber
            Assert.IsType<ForbidResult>(result);
        }

        [Fact]
        public async Task Vitals_Create_Post_CrossChamberDoctor_ReturnsForbid()
        {
            // Arrange: Dr Bob (Id: 11) has an appointment
            var context = await TestDbContextFactory.CreateSeededDbContextAsync();
            var bobsAppointment = new Appointment
            {
                Id = 200,
                PatientId = 100,
                DoctorId = 11, // Dr Bob
                AppointmentDatetime = DateTime.UtcNow.AddHours(1),
                EndTime = DateTime.UtcNow.AddHours(1).AddMinutes(15),
                Status = AppointmentStatus.Scheduled,
                ReasonForVisit = "Dr Bob Appointment",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            context.Appointments.Add(bobsAppointment);
            await context.SaveChangesAsync();

            // Act: Assistant Anna (AssignedDoctorId: 10) posts vitals for Dr Bob's appointment
            var controller = new VitalsController(context);
            ControllerTestHelper.SetupController(controller, TestPrincipalFactory.CreateAssistant(assistantId: 20, assignedDoctorId: 10));

            var vital = new PatientVital
            {
                AppointmentId = 200,
                PatientId = 100,
                SystolicBp = 120,
                DiastolicBp = 80,
                HeartRate = 72,
                RespiratoryRate = 16,
                Spo2 = 98.0m,
                Temperature = 36.6m
            };

            var result = await controller.Create(vital);

            // Assert: Rejected with ForbidResult
            Assert.IsType<ForbidResult>(result);
            Assert.Equal(0, await context.PatientVitals.CountAsync());
        }

        [Fact]
        public async Task Appointments_SendInToDoctor_CrossChamberAssistant_ReturnsForbid()
        {
            // Arrange: Dr Bob's scheduled appointment
            var context = await TestDbContextFactory.CreateSeededDbContextAsync();
            var bobsAppointment = new Appointment
            {
                Id = 200,
                PatientId = 100,
                DoctorId = 11, // Dr Bob
                AppointmentDatetime = DateTime.UtcNow.AddHours(1),
                EndTime = DateTime.UtcNow.AddHours(1).AddMinutes(15),
                Status = AppointmentStatus.Scheduled,
                ReasonForVisit = "Consultation",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            context.Appointments.Add(bobsAppointment);
            await context.SaveChangesAsync();

            // Act: Assistant Anna (AssignedDoctorId: 10) attempts to send patient in to Dr Bob's chamber
            var controller = new AppointmentsController(
                context,
                new TestHubContext<NotificationHub>(),
                new TestServiceScopeFactory(),
                NullLogger<AppointmentsController>.Instance);
            ControllerTestHelper.SetupController(controller, TestPrincipalFactory.CreateAssistant(assistantId: 20, assignedDoctorId: 10));

            var result = await controller.SendIn(200);

            // Assert: Cross-chamber send-in is forbidden
            Assert.IsType<ForbidResult>(result);

            // Verify status was NOT updated to InConsultation
            var dbAppt = await context.Appointments.FindAsync(200);
            Assert.Equal(AppointmentStatus.Scheduled, dbAppt!.Status);
        }

        [Fact]
        public async Task Patients_Details_UnassignedAssistant_ReturnsForbid()
        {
            // Arrange: Seeded context with unassigned assistant
            var context = await TestDbContextFactory.CreateSeededDbContextAsync();
            var controller = new PatientsController(context);
            ControllerTestHelper.SetupController(controller, TestPrincipalFactory.CreateUnassignedAssistant(assistantId: 21));

            // Act: Attempt to access patient details
            var result = await controller.Details(100);

            // Assert: Blocked because assistant has no assigned doctor
            Assert.IsType<ForbidResult>(result);
        }

        [Fact]
        public async Task Patients_Edit_UnassignedAssistant_ReturnsForbid()
        {
            // Arrange: Seeded context with unassigned assistant
            var context = await TestDbContextFactory.CreateSeededDbContextAsync();
            var controller = new PatientsController(context);
            ControllerTestHelper.SetupController(controller, TestPrincipalFactory.CreateUnassignedAssistant(assistantId: 21));

            // Act: Attempt to edit patient
            var result = await controller.Edit(100);

            // Assert: Blocked
            Assert.IsType<ForbidResult>(result);
        }

        [Fact]
        public async Task Patients_Details_AssistantAssignedToCaringDoctor_Allowed()
        {
            // Arrange: Dr Alice has an appointment with Patient 100
            var context = await TestDbContextFactory.CreateSeededDbContextAsync();
            var aliceAppointment = new Appointment
            {
                Id = 201,
                PatientId = 100,
                DoctorId = 10, // Dr Alice
                AppointmentDatetime = DateTime.UtcNow.AddHours(1),
                EndTime = DateTime.UtcNow.AddHours(1).AddMinutes(15),
                Status = AppointmentStatus.Scheduled,
                ReasonForVisit = "Alice Follow-up",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            context.Appointments.Add(aliceAppointment);
            await context.SaveChangesAsync();

            // Act: Assistant Anna (AssignedDoctorId: 10) accesses Patient 100 details
            var controller = new PatientsController(context);
            ControllerTestHelper.SetupController(controller, TestPrincipalFactory.CreateAssistant(assistantId: 20, assignedDoctorId: 10));

            var result = await controller.Details(100);

            // Assert: Care relationship via assigned doctor is valid, access granted
            var viewResult = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<Patient>(viewResult.Model);
            Assert.Equal(100, model.Id);
            Assert.Equal("John Doe", model.FullName);
        }
    }
}
