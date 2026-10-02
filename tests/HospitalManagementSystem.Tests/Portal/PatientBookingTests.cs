using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
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

namespace HospitalManagementSystem.Tests.Portal
{
    public class PatientBookingTests
    {
        private async Task<(ApplicationDbContext Context, Patient PatientA, Patient PatientB, User DoctorA, User DoctorB)> SetupTestEnvironmentAsync()
        {
            var context = await TestDbContextFactory.CreateSeededDbContextAsync();

            var patientA = await context.Patients.FindAsync(100);
            patientA!.UserId = 1000;

            var patientB = await context.Patients.FindAsync(101);
            patientB!.UserId = 1001;

            var doctorA = await context.Users.FindAsync(10);
            doctorA!.Category = "Cardiology";

            var doctorB = await context.Users.FindAsync(11);
            doctorB!.Category = "Neurology";

            await context.SaveChangesAsync();

            return (context, patientA, patientB, doctorA, doctorB);
        }

        private PortalController CreatePortalController(ApplicationDbContext context, ClaimsPrincipal user)
        {
            var service = new DoctorScheduleService(context);
            var controller = new PortalController(context, service);
            ControllerTestHelper.SetupController(controller, user);
            return controller;
        }

        [Fact]
        public async Task DoctorScheduleService_Generates15MinSlotsAcrossWorkingHours()
        {
            // Arrange
            var (context, _, _, doctorA, _) = await SetupTestEnvironmentAsync();
            var service = new DoctorScheduleService(context);

            // A future date guarantees no lead-time clipping
            var futureDate = HospitalClock.Today.AddDays(7);

            // Act
            var result = await service.GetDailySlotsAsync(doctorA.Id, futureDate);

            // Assert: 9:00 AM to 5:00 PM = 8 hours * 4 slots/hr = 32 slots total
            Assert.NotNull(result);
            Assert.Equal(doctorA.Id, result.DoctorId);
            Assert.Equal(16, result.MorningSlots.Count);    // 09:00 - 12:45
            Assert.Equal(16, result.AfternoonSlots.Count);  // 13:00 - 16:45
            Assert.Equal(32, result.TotalAvailable);

            // Check first morning slot and last afternoon slot
            Assert.Equal("09:00 AM", result.MorningSlots.First().TimeDisplay);
            Assert.Equal("12:45 PM", result.MorningSlots.Last().TimeDisplay);
            Assert.Equal("01:00 PM", result.AfternoonSlots.First().TimeDisplay);
            Assert.Equal("04:45 PM", result.AfternoonSlots.Last().TimeDisplay);
        }

        [Fact]
        public async Task DoctorScheduleService_MarksConflictingSlotsUnavailable()
        {
            // Arrange: Book doctor A for tomorrow at 10:15 AM local
            var (context, patientA, _, doctorA, _) = await SetupTestEnvironmentAsync();
            var service = new DoctorScheduleService(context);

            var tomorrow = HospitalClock.Today.AddDays(2);
            var slotLocalStart = tomorrow.AddHours(10).AddMinutes(15);
            var slotUtcStart = TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(slotLocalStart, DateTimeKind.Unspecified),
                HospitalClock.TimeZone);

            var existing = new Appointment
            {
                DoctorId = doctorA.Id,
                PatientId = patientA.Id,
                AppointmentDatetime = slotUtcStart,
                EndTime = slotUtcStart.AddMinutes(15),
                Status = AppointmentStatus.Scheduled,
                ReasonForVisit = "Cardiology follow up",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            context.Appointments.Add(existing);
            await context.SaveChangesAsync();

            // Act
            var result = await service.GetDailySlotsAsync(doctorA.Id, tomorrow);

            // Assert: The 10:15 AM slot is booked; 10:00 AM and 10:30 AM are available
            var bookedSlot = result.MorningSlots.FirstOrDefault(s => s.TimeDisplay == "10:15 AM");
            Assert.NotNull(bookedSlot);
            Assert.False(bookedSlot.IsAvailable);
            Assert.Equal("Booked", bookedSlot.ConflictReason);

            var prevSlot = result.MorningSlots.FirstOrDefault(s => s.TimeDisplay == "10:00 AM");
            Assert.NotNull(prevSlot);
            Assert.True(prevSlot.IsAvailable);

            var nextSlot = result.MorningSlots.FirstOrDefault(s => s.TimeDisplay == "10:30 AM");
            Assert.NotNull(nextSlot);
            Assert.True(nextSlot.IsAvailable);

            Assert.Equal(31, result.TotalAvailable);
        }

        [Fact]
        public async Task DoctorScheduleService_MarksPastSlotsUnavailable()
        {
            // Arrange: Check slots for yesterday
            var (context, _, _, doctorA, _) = await SetupTestEnvironmentAsync();
            var service = new DoctorScheduleService(context);
            var yesterday = HospitalClock.Today.AddDays(-1);

            // Act
            var result = await service.GetDailySlotsAsync(doctorA.Id, yesterday);

            // Assert: All slots in the past must be unavailable with reason "Past slot"
            Assert.All(result.MorningSlots, s =>
            {
                Assert.False(s.IsAvailable);
                Assert.Equal("Past slot", s.ConflictReason);
            });
            Assert.All(result.AfternoonSlots, s =>
            {
                Assert.False(s.IsAvailable);
                Assert.Equal("Past slot", s.ConflictReason);
            });
            Assert.Equal(0, result.TotalAvailable);
        }

        [Fact]
        public async Task DoctorScheduleService_GetDoctorDirectory_FiltersBySearchAndCategory()
        {
            // Arrange
            var (context, _, _, _, _) = await SetupTestEnvironmentAsync();
            var service = new DoctorScheduleService(context);

            // Act 1: Search by cardiology category
            var cardioDocs = await service.GetDoctorDirectoryAsync(category: "Cardiology");
            Assert.Single(cardioDocs);
            Assert.Equal("Dr. Alice", cardioDocs[0].FullName);

            // Act 2: Search by doctor name substring
            var bobDocs = await service.GetDoctorDirectoryAsync(search: "bob");
            Assert.Single(bobDocs);
            Assert.Equal("Dr. Bob", bobDocs[0].FullName);

            // Act 3: Filter All
            var allDocs = await service.GetDoctorDirectoryAsync();
            Assert.Equal(2, allDocs.Count);
        }

        [Fact]
        public async Task Portal_Book_ValidSubmission_CreatesScheduledAppointment()
        {
            // Arrange
            var (context, patientA, _, doctorA, _) = await SetupTestEnvironmentAsync();
            var userPrincipal = TestPrincipalFactory.CreatePatient(patientUserId: 1000, uhid: patientA.Uhid);
            var controller = CreatePortalController(context, userPrincipal);

            var futureDate = HospitalClock.Today.AddDays(3);
            var slotLocalStart = futureDate.AddHours(11).AddMinutes(0);
            var slotUtcStart = TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(slotLocalStart, DateTimeKind.Unspecified),
                HospitalClock.TimeZone);

            var model = new BookAppointmentViewModel
            {
                DoctorId = doctorA.Id,
                SelectedSlotTime = slotUtcStart.ToString("O"),
                ReasonForVisit = "Annual health checkup",
                SelectedDate = futureDate.ToString("yyyy-MM-dd")
            };

            // Act
            var result = await controller.Book(model);

            // Assert
            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Index", redirect.ActionName);

            var created = await context.Appointments.FirstOrDefaultAsync(a => a.DoctorId == doctorA.Id && a.PatientId == patientA.Id);
            Assert.NotNull(created);
            Assert.Equal(slotUtcStart, created.AppointmentDatetime);
            Assert.Equal(slotUtcStart.AddMinutes(15), created.EndTime);
            Assert.Equal(AppointmentStatus.Scheduled, created.Status);
            Assert.Equal("Annual health checkup", created.ReasonForVisit);
        }

        [Fact]
        public async Task Portal_Book_ConflictingSlot_RejectsWithValidationError()
        {
            // Arrange: Existing appointment for doctorA at 14:00 local
            var (context, patientA, patientB, doctorA, _) = await SetupTestEnvironmentAsync();
            var futureDate = HospitalClock.Today.AddDays(3);
            var slotLocalStart = futureDate.AddHours(14).AddMinutes(0);
            var slotUtcStart = TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(slotLocalStart, DateTimeKind.Unspecified),
                HospitalClock.TimeZone);

            var existing = new Appointment
            {
                DoctorId = doctorA.Id,
                PatientId = patientB.Id,
                AppointmentDatetime = slotUtcStart,
                EndTime = slotUtcStart.AddMinutes(15),
                Status = AppointmentStatus.Scheduled,
                ReasonForVisit = "Existing booking",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            context.Appointments.Add(existing);
            await context.SaveChangesAsync();

            var userPrincipal = TestPrincipalFactory.CreatePatient(patientUserId: 1000, uhid: patientA.Uhid);
            var controller = CreatePortalController(context, userPrincipal);

            var model = new BookAppointmentViewModel
            {
                DoctorId = doctorA.Id,
                SelectedSlotTime = slotUtcStart.ToString("O"),
                ReasonForVisit = "Patient A trying same slot",
                SelectedDate = futureDate.ToString("yyyy-MM-dd")
            };

            // Act
            var result = await controller.Book(model);

            // Assert: View returned with validation error
            var viewResult = Assert.IsType<ViewResult>(result);
            Assert.False(controller.ModelState.IsValid);
            Assert.Contains(controller.ModelState.Values, v => v.Errors.Any(e => e.ErrorMessage.Contains("booked by another patient")));

            // Ensure no duplicate appointment created
            Assert.Equal(1, await context.Appointments.CountAsync(a => a.DoctorId == doctorA.Id));
        }

        [Fact]
        public async Task Portal_Book_PatientDoubleBooking_RejectsWithValidationError()
        {
            // Arrange: Patient A already has an appointment with Doctor B at 10:00 AM
            var (context, patientA, _, doctorA, doctorB) = await SetupTestEnvironmentAsync();
            var futureDate = HospitalClock.Today.AddDays(3);
            var slotLocalStart = futureDate.AddHours(10).AddMinutes(0);
            var slotUtcStart = TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(slotLocalStart, DateTimeKind.Unspecified),
                HospitalClock.TimeZone);

            var existingAppointment = new Appointment
            {
                DoctorId = doctorB.Id,
                PatientId = patientA.Id,
                AppointmentDatetime = slotUtcStart,
                EndTime = slotUtcStart.AddMinutes(15),
                Status = AppointmentStatus.Scheduled,
                ReasonForVisit = "With Doctor B",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            context.Appointments.Add(existingAppointment);
            await context.SaveChangesAsync();

            // Act: Patient A attempts to book Doctor A at the exact same time
            var userPrincipal = TestPrincipalFactory.CreatePatient(patientUserId: 1000, uhid: patientA.Uhid);
            var controller = CreatePortalController(context, userPrincipal);

            var model = new BookAppointmentViewModel
            {
                DoctorId = doctorA.Id,
                SelectedSlotTime = slotUtcStart.ToString("O"),
                ReasonForVisit = "Double booking attempt",
                SelectedDate = futureDate.ToString("yyyy-MM-dd")
            };

            var result = await controller.Book(model);

            // Assert: Rejected due to active appointment conflict
            var viewResult = Assert.IsType<ViewResult>(result);
            Assert.False(controller.ModelState.IsValid);
            Assert.Contains(controller.ModelState.Values, v => v.Errors.Any(e => e.ErrorMessage.Contains("already have another active appointment scheduled")));
        }

        [Fact]
        public async Task Portal_Reschedule_OwnAppointment_UpdatesDatetime()
        {
            // Arrange: Patient A has an upcoming scheduled appointment
            var (context, patientA, _, doctorA, _) = await SetupTestEnvironmentAsync();
            var futureDate = HospitalClock.Today.AddDays(3);
            var oldLocalStart = futureDate.AddHours(10).AddMinutes(0);
            var oldUtcStart = TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(oldLocalStart, DateTimeKind.Unspecified),
                HospitalClock.TimeZone);

            var appt = new Appointment
            {
                DoctorId = doctorA.Id,
                PatientId = patientA.Id,
                AppointmentDatetime = oldUtcStart,
                EndTime = oldUtcStart.AddMinutes(15),
                Status = AppointmentStatus.Scheduled,
                ReasonForVisit = "Original reason",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            context.Appointments.Add(appt);
            await context.SaveChangesAsync();

            var newLocalStart = futureDate.AddHours(15).AddMinutes(30);
            var newUtcStart = TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(newLocalStart, DateTimeKind.Unspecified),
                HospitalClock.TimeZone);

            var userPrincipal = TestPrincipalFactory.CreatePatient(patientUserId: 1000, uhid: patientA.Uhid);
            var controller = CreatePortalController(context, userPrincipal);

            var model = new RescheduleAppointmentViewModel
            {
                AppointmentId = appt.Id,
                DoctorId = doctorA.Id,
                SelectedSlotTime = newUtcStart.ToString("O"),
                ReasonForVisit = "Updated reason - rescheduled",
                Version = appt.Version
            };

            // Act
            var result = await controller.Reschedule(model);

            // Assert: Successfully rescheduled
            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Index", redirect.ActionName);

            var updated = await context.Appointments.FindAsync(appt.Id);
            Assert.Equal(newUtcStart, updated!.AppointmentDatetime);
            Assert.Equal(newUtcStart.AddMinutes(15), updated.EndTime);
            Assert.Equal("Updated reason - rescheduled", updated.ReasonForVisit);
        }

        [Fact]
        public async Task Portal_Reschedule_CrossPatientAppointment_ReturnsNotFound()
        {
            // Arrange: Appointment belongs to Patient A
            var (context, patientA, patientB, doctorA, _) = await SetupTestEnvironmentAsync();
            var futureDate = HospitalClock.Today.AddDays(3);
            var slotLocal = futureDate.AddHours(10);
            var slotUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(slotLocal, DateTimeKind.Unspecified), HospitalClock.TimeZone);

            var appt = new Appointment
            {
                DoctorId = doctorA.Id,
                PatientId = patientA.Id,
                AppointmentDatetime = slotUtc,
                EndTime = slotUtc.AddMinutes(15),
                Status = AppointmentStatus.Scheduled,
                ReasonForVisit = "Patient A Visit",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            context.Appointments.Add(appt);
            await context.SaveChangesAsync();

            // Act: Patient B attempts to reschedule Patient A's appointment
            var patientBPrincipal = TestPrincipalFactory.CreatePatient(patientUserId: 1001, uhid: patientB.Uhid);
            var controller = CreatePortalController(context, patientBPrincipal);

            var model = new RescheduleAppointmentViewModel
            {
                AppointmentId = appt.Id,
                DoctorId = doctorA.Id,
                SelectedSlotTime = slotUtc.AddHours(1).ToString("O")
            };

            var getResult = await controller.Reschedule(appt.Id);
            var postResult = await controller.Reschedule(model);

            // Assert: Cannot view or edit another patient's appointment
            Assert.IsType<NotFoundResult>(getResult);
            Assert.IsType<NotFoundResult>(postResult);

            // Original appointment remains untouched
            var unchanged = await context.Appointments.FindAsync(appt.Id);
            Assert.Equal(slotUtc, unchanged!.AppointmentDatetime);
        }

        [Fact]
        public async Task Portal_Reschedule_TerminalAppointment_ReturnsValidationError()
        {
            // Arrange: Patient A has an appointment that is already Completed
            var (context, patientA, _, doctorA, _) = await SetupTestEnvironmentAsync();
            var futureDate = HospitalClock.Today.AddDays(3);
            var slotUtc = DateTime.UtcNow.AddDays(1);

            var appt = new Appointment
            {
                DoctorId = doctorA.Id,
                PatientId = patientA.Id,
                AppointmentDatetime = slotUtc,
                EndTime = slotUtc.AddMinutes(15),
                Status = AppointmentStatus.Completed,
                ReasonForVisit = "Done",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            context.Appointments.Add(appt);
            await context.SaveChangesAsync();

            var userPrincipal = TestPrincipalFactory.CreatePatient(patientUserId: 1000, uhid: patientA.Uhid);
            var controller = CreatePortalController(context, userPrincipal);

            // Act GET: should redirect with error
            var getResult = await controller.Reschedule(appt.Id);
            var redirect = Assert.IsType<RedirectToActionResult>(getResult);
            Assert.Equal("Index", redirect.ActionName);
            Assert.Equal("Only upcoming scheduled appointments can be rescheduled.", controller.TempData["ErrorMessage"]);

            // Act POST: should return view with validation error
            var model = new RescheduleAppointmentViewModel
            {
                AppointmentId = appt.Id,
                DoctorId = doctorA.Id,
                SelectedSlotTime = slotUtc.AddDays(1).ToString("O")
            };
            var postResult = await controller.Reschedule(model);
            var viewResult = Assert.IsType<ViewResult>(postResult);
            Assert.False(controller.ModelState.IsValid);
        }

        [Fact]
        public async Task Portal_Cancel_OwnAppointment_TransitionsToCancelled()
        {
            // Arrange: Patient A has a scheduled future appointment
            var (context, patientA, _, doctorA, _) = await SetupTestEnvironmentAsync();
            var futureDate = HospitalClock.Today.AddDays(3);
            var slotLocal = futureDate.AddHours(14);
            var slotUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(slotLocal, DateTimeKind.Unspecified), HospitalClock.TimeZone);

            var appt = new Appointment
            {
                DoctorId = doctorA.Id,
                PatientId = patientA.Id,
                AppointmentDatetime = slotUtc,
                EndTime = slotUtc.AddMinutes(15),
                Status = AppointmentStatus.Scheduled,
                ReasonForVisit = "To cancel",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            context.Appointments.Add(appt);
            await context.SaveChangesAsync();

            var userPrincipal = TestPrincipalFactory.CreatePatient(patientUserId: 1000, uhid: patientA.Uhid);
            var controller = CreatePortalController(context, userPrincipal);

            // Act
            var result = await controller.Cancel(appt.Id);

            // Assert
            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Index", redirect.ActionName);

            var updated = await context.Appointments.FindAsync(appt.Id);
            Assert.Equal(AppointmentStatus.Cancelled, updated!.Status);

            // Verify the slot is now free
            var service = new DoctorScheduleService(context);
            var dailySlots = await service.GetDailySlotsAsync(doctorA.Id, futureDate);
            var freedSlot = dailySlots.AfternoonSlots.FirstOrDefault(s => s.TimeDisplay == "02:00 PM");
            Assert.NotNull(freedSlot);
            Assert.True(freedSlot.IsAvailable);
        }

        [Fact]
        public async Task Portal_Cancel_CrossPatientAppointment_ReturnsNotFound()
        {
            // Arrange: Appointment belongs to Patient A
            var (context, patientA, patientB, doctorA, _) = await SetupTestEnvironmentAsync();
            var futureDate = HospitalClock.Today.AddDays(3);
            var slotLocal = futureDate.AddHours(14);
            var slotUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(slotLocal, DateTimeKind.Unspecified), HospitalClock.TimeZone);

            var appt = new Appointment
            {
                DoctorId = doctorA.Id,
                PatientId = patientA.Id,
                AppointmentDatetime = slotUtc,
                EndTime = slotUtc.AddMinutes(15),
                Status = AppointmentStatus.Scheduled,
                ReasonForVisit = "Protected visit",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            context.Appointments.Add(appt);
            await context.SaveChangesAsync();

            // Act: Patient B tries to cancel Patient A's visit
            var patientBPrincipal = TestPrincipalFactory.CreatePatient(patientUserId: 1001, uhid: patientB.Uhid);
            var controller = CreatePortalController(context, patientBPrincipal);

            var result = await controller.Cancel(appt.Id);

            // Assert: NotFound and status is still Scheduled
            Assert.IsType<NotFoundResult>(result);

            var unchanged = await context.Appointments.FindAsync(appt.Id);
            Assert.Equal(AppointmentStatus.Scheduled, unchanged!.Status);
        }

        [Fact]
        public async Task Portal_Cancel_InConsultationAppointment_Rejected()
        {
            // Arrange: Appointment is already InConsultation
            var (context, patientA, _, doctorA, _) = await SetupTestEnvironmentAsync();
            var slotUtc = DateTime.UtcNow.AddMinutes(5);

            var appt = new Appointment
            {
                DoctorId = doctorA.Id,
                PatientId = patientA.Id,
                AppointmentDatetime = slotUtc,
                EndTime = slotUtc.AddMinutes(15),
                Status = AppointmentStatus.InConsultation,
                ReasonForVisit = "Underway",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            context.Appointments.Add(appt);
            await context.SaveChangesAsync();

            var userPrincipal = TestPrincipalFactory.CreatePatient(patientUserId: 1000, uhid: patientA.Uhid);
            var controller = CreatePortalController(context, userPrincipal);

            // Act
            var result = await controller.Cancel(appt.Id);

            // Assert: Cannot cancel in-consultation appointment
            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Index", redirect.ActionName);
            Assert.Contains("Only scheduled visits can be cancelled", (string)controller.TempData["ErrorMessage"]!);

            var unchanged = await context.Appointments.FindAsync(appt.Id);
            Assert.Equal(AppointmentStatus.InConsultation, unchanged!.Status);
        }

        [Fact]
        public async Task Portal_GetAvailableSlots_ReturnsValidJsonSlotStructure()
        {
            // Arrange
            var (context, patientA, _, doctorA, _) = await SetupTestEnvironmentAsync();
            var userPrincipal = TestPrincipalFactory.CreatePatient(patientUserId: 1000, uhid: patientA.Uhid);
            var controller = CreatePortalController(context, userPrincipal);

            var targetDate = HospitalClock.Today.AddDays(4).ToString("yyyy-MM-dd");

            // Act
            var result = await controller.GetAvailableSlots(doctorA.Id, targetDate);

            // Assert
            var jsonResult = Assert.IsType<JsonResult>(result);
            var data = Assert.IsType<DailySlotsResponseDto>(jsonResult.Value);
            Assert.Equal(doctorA.Id, data.DoctorId);
            Assert.Equal(targetDate, data.Date);
            Assert.Equal(16, data.MorningSlots.Count);
            Assert.Equal(16, data.AfternoonSlots.Count);
            Assert.Equal(32, data.TotalAvailable);
        }
    }
}
