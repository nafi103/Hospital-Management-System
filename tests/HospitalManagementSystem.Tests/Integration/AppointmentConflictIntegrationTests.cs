using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using HospitalManagementSystem.Controllers;
using HospitalManagementSystem.Hubs;
using HospitalManagementSystem.Models;
using HospitalManagementSystem.Services;
using HospitalManagementSystem.Tests.Fixtures;
using HospitalManagementSystem.Tests.Helpers;
using Xunit;

namespace HospitalManagementSystem.Tests.Integration
{
    public class AppointmentConflictIntegrationTests
    {
        private async Task<(ApplicationDbContext Context, AppointmentsController Controller)> CreateTestSetupAsync(ClaimsPrincipal user)
        {
            var context = await TestDbContextFactory.CreateSeededDbContextAsync();
            var hubContext = new TestHubContext<NotificationHub>();
            var scopeFactory = new TestServiceScopeFactory();
            var logger = NullLogger<AppointmentsController>.Instance;

            var controller = new AppointmentsController(context, hubContext, scopeFactory, logger);
            ControllerTestHelper.SetupController(controller, user);
            return (context, controller);
        }

        [Fact]
        public async Task Create_OverlappingSlotSameDoctor_ReturnsConflictValidationError()
        {
            // Arrange: Dr Alice (Id: 10) has an active appointment at 14:00 local tomorrow
            var (context, controller) = await CreateTestSetupAsync(TestPrincipalFactory.CreateReceptionist());

            var tomorrow = DateTime.UtcNow.Date.AddDays(1);
            var existingLocal = tomorrow.AddHours(14); // 2:00 PM local
            var existingUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(existingLocal, DateTimeKind.Unspecified), HospitalClock.TimeZone);

            var existingAppointment = new Appointment
            {
                PatientId = 100,
                DoctorId = 10,
                AppointmentDatetime = existingUtc,
                EndTime = existingUtc.AddMinutes(15),
                Status = AppointmentStatus.Scheduled,
                ReasonForVisit = "Annual Checkup",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            context.Appointments.Add(existingAppointment);
            await context.SaveChangesAsync();

            // Act: Attempt to schedule a new appointment overlapping existing one (same start time in local)
            var newAppointment = new Appointment
            {
                PatientId = 101,
                DoctorId = 10,
                AppointmentDatetime = existingLocal, // Controller will convert this using HospitalClock
                ReasonForVisit = "Follow-up"
            };

            var result = await controller.Create(newAppointment);

            // Assert: View returned with Schedule Conflict ModelState error
            var viewResult = Assert.IsType<ViewResult>(result);
            Assert.False(controller.ModelState.IsValid);
            Assert.True(controller.ModelState.ContainsKey("AppointmentDatetime"));
            var error = controller.ModelState["AppointmentDatetime"]!.Errors.First().ErrorMessage;
            Assert.Contains("Schedule Conflict", error);
            Assert.Contains("Doctor already has an appointment booked", error);

            // Verify only 1 appointment exists in the DB
            Assert.Equal(1, await context.Appointments.CountAsync());
        }

        [Fact]
        public async Task Create_BackToBackSlotSameDoctor_IsAllowed()
        {
            // Arrange: Dr Alice has appointment 14:00 - 14:15. New appointment requested at 14:15.
            var (context, controller) = await CreateTestSetupAsync(TestPrincipalFactory.CreateReceptionist());

            var tomorrow = DateTime.UtcNow.Date.AddDays(1);
            var firstLocal = tomorrow.AddHours(14); // 2:00 PM local
            var firstUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(firstLocal, DateTimeKind.Unspecified), HospitalClock.TimeZone);

            var existingAppointment = new Appointment
            {
                PatientId = 100,
                DoctorId = 10,
                AppointmentDatetime = firstUtc,
                EndTime = firstUtc.AddMinutes(15),
                Status = AppointmentStatus.Scheduled,
                ReasonForVisit = "First Appointment",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            context.Appointments.Add(existingAppointment);
            await context.SaveChangesAsync();

            // Act: Schedule immediately adjacent slot (14:15 local)
            var secondLocal = firstLocal.AddMinutes(15);
            var newAppointment = new Appointment
            {
                PatientId = 101,
                DoctorId = 10,
                AppointmentDatetime = secondLocal,
                ReasonForVisit = "Back-to-back Appointment"
            };

            var result = await controller.Create(newAppointment);

            // Assert: Successfully created and redirected to Index
            var redirectResult = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Index", redirectResult.ActionName);
            Assert.True(controller.ModelState.IsValid);
            Assert.Equal(2, await context.Appointments.CountAsync());
        }

        [Fact]
        public async Task Create_SameTimeDifferentDoctor_IsAllowed()
        {
            // Arrange: Dr Alice has an appointment. New appointment requested at exact same time for Dr Bob (Id: 11)
            var (context, controller) = await CreateTestSetupAsync(TestPrincipalFactory.CreateReceptionist());

            var tomorrow = DateTime.UtcNow.Date.AddDays(1);
            var slotLocal = tomorrow.AddHours(10);
            var slotUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(slotLocal, DateTimeKind.Unspecified), HospitalClock.TimeZone);

            var aliceAppt = new Appointment
            {
                PatientId = 100,
                DoctorId = 10, // Dr Alice
                AppointmentDatetime = slotUtc,
                EndTime = slotUtc.AddMinutes(15),
                Status = AppointmentStatus.Scheduled,
                ReasonForVisit = "Alice's Patient",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            context.Appointments.Add(aliceAppt);
            await context.SaveChangesAsync();

            // Act: Schedule for Dr Bob at same slot
            var bobAppt = new Appointment
            {
                PatientId = 101,
                DoctorId = 11, // Dr Bob
                AppointmentDatetime = slotLocal,
                ReasonForVisit = "Bob's Patient"
            };

            var result = await controller.Create(bobAppt);

            // Assert: Redirected to Index with success
            var redirectResult = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Index", redirectResult.ActionName);
            Assert.True(controller.ModelState.IsValid);
            Assert.Equal(2, await context.Appointments.CountAsync());
        }

        [Fact]
        public async Task Create_SameTimeCancelledOrCompletedAppointment_IsAllowed()
        {
            // Arrange: Dr Alice had an appointment that was Cancelled
            var (context, controller) = await CreateTestSetupAsync(TestPrincipalFactory.CreateReceptionist());

            var tomorrow = DateTime.UtcNow.Date.AddDays(1);
            var slotLocal = tomorrow.AddHours(11);
            var slotUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(slotLocal, DateTimeKind.Unspecified), HospitalClock.TimeZone);

            var cancelledAppt = new Appointment
            {
                PatientId = 100,
                DoctorId = 10,
                AppointmentDatetime = slotUtc,
                EndTime = slotUtc.AddMinutes(15),
                Status = AppointmentStatus.Cancelled,
                ReasonForVisit = "Cancelled Visit",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            context.Appointments.Add(cancelledAppt);
            await context.SaveChangesAsync();

            // Act: Schedule a new active appointment in the same cancelled slot
            var newAppt = new Appointment
            {
                PatientId = 101,
                DoctorId = 10,
                AppointmentDatetime = slotLocal,
                ReasonForVisit = "Replacement Visit"
            };

            var result = await controller.Create(newAppt);

            // Assert: Allowed
            var redirectResult = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Index", redirectResult.ActionName);
            Assert.True(controller.ModelState.IsValid);
        }

        [Fact]
        public async Task Create_AssistantScheduling_ForcesDoctorIdToAssignedDoctor()
        {
            // Arrange: Assistant Anna (AssignedDoctorId: 10) attempts to book for Dr Bob (Id: 11)
            var assistantPrincipal = TestPrincipalFactory.CreateAssistant(assistantId: 20, assignedDoctorId: 10);
            var (context, controller) = await CreateTestSetupAsync(assistantPrincipal);

            var tomorrow = DateTime.UtcNow.Date.AddDays(1).AddHours(15);
            var appt = new Appointment
            {
                PatientId = 100,
                DoctorId = 11, // Attempted to assign Dr Bob
                AppointmentDatetime = tomorrow,
                ReasonForVisit = "Assistant Booking"
            };

            // Act:
            var result = await controller.Create(appt);

            // Assert: Controller forces DoctorId to 10 (Dr Alice)
            var redirectResult = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Index", redirectResult.ActionName);

            var saved = await context.Appointments.OrderByDescending(a => a.Id).FirstOrDefaultAsync();
            Assert.NotNull(saved);
            Assert.Equal(10, saved.DoctorId); // Overridden to assigned doctor!
            Assert.Equal(AppointmentStatus.Scheduled, saved.Status);
        }

        [Fact]
        public async Task Edit_TerminalAppointmentStatusChange_IsRejected()
        {
            // Arrange: Completed appointment
            var (context, controller) = await CreateTestSetupAsync(TestPrincipalFactory.CreateDoctor(doctorId: 10));

            var appt = new Appointment
            {
                PatientId = 100,
                DoctorId = 10,
                AppointmentDatetime = DateTime.UtcNow.AddHours(-2),
                EndTime = DateTime.UtcNow.AddHours(-1).AddMinutes(45),
                Status = AppointmentStatus.Completed,
                ReasonForVisit = "Done",
                CreatedAt = DateTime.UtcNow.AddHours(-3),
                UpdatedAt = DateTime.UtcNow.AddHours(-1)
            };
            context.Appointments.Add(appt);
            await context.SaveChangesAsync();

            // Act: Try to edit status back to Scheduled
            var editModel = new Appointment
            {
                Id = appt.Id,
                PatientId = 100,
                DoctorId = 10,
                Status = AppointmentStatus.Scheduled,
                ReasonForVisit = "Attempted Reopen"
            };

            var result = await controller.Edit(appt.Id, editModel);

            // Assert: Rejected by terminal state machine guard
            var viewResult = Assert.IsType<ViewResult>(result);
            Assert.False(controller.ModelState.IsValid);
            Assert.True(controller.ModelState.ContainsKey("Status"));
            Assert.Contains("Terminal appointments cannot be reopened", controller.ModelState["Status"]!.Errors.First().ErrorMessage);

            // Verify status in DB remains Completed
            var dbAppt = await context.Appointments.FindAsync(appt.Id);
            Assert.Equal(AppointmentStatus.Completed, dbAppt!.Status);
        }
    }
}
