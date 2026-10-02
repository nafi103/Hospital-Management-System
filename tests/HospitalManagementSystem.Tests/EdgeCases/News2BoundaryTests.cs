using System;
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

namespace HospitalManagementSystem.Tests.EdgeCases
{
    public class News2BoundaryTests
    {
        [Fact]
        public void News2Calculator_MaximumExtremeParameters_ScoresTwentyAndIsEmergency()
        {
            // All parameters at their most severe physiological extremes
            var result = News2Calculator.Calculate(
                respiratoryRate: 45,            // +3
                spo2: 75,                       // +3
                onSupplementalOxygen: true,     // +2
                temperature: 32.0m,             // +3
                systolicBp: 60,                 // +3
                heartRate: 180,                 // +3
                consciousness: ConsciousnessLevel.Unresponsive // +3
            );

            Assert.Equal(20, result.Score);
            Assert.Equal(TriagePriority.Emergency, result.Priority);
        }

        [Theory]
        [InlineData(30, 80, 70, 16, 98.0, 37.0, 5.0, "Systolic BP must be between 40 and 260 mmHg.")]
        [InlineData(280, 80, 70, 16, 98.0, 37.0, 5.0, "Systolic BP must be between 40 and 260 mmHg.")]
        [InlineData(120, 15, 70, 16, 98.0, 37.0, 5.0, "Diastolic BP must be between 20 and 180 mmHg.")]
        [InlineData(120, 80, 20, 16, 98.0, 37.0, 5.0, "Heart Rate must be between 25 and 250 bpm.")]
        [InlineData(120, 80, 260, 16, 98.0, 37.0, 5.0, "Heart Rate must be between 25 and 250 bpm.")]
        [InlineData(120, 80, 70, 2, 98.0, 37.0, 5.0, "Respiratory Rate must be between 4 and 60 breaths/min.")]
        [InlineData(120, 80, 70, 75, 98.0, 37.0, 5.0, "Respiratory Rate must be between 4 and 60 breaths/min.")]
        [InlineData(120, 80, 70, 16, 45.0, 37.0, 5.0, "SpO2 must be between 50% and 100%.")]
        [InlineData(120, 80, 70, 16, 98.0, 28.0, 5.0, "Temperature must be between 30.0°C and 45.0°C.")]
        [InlineData(120, 80, 70, 16, 98.0, 48.0, 5.0, "Temperature must be between 30.0°C and 45.0°C.")]
        [InlineData(120, 80, 70, 16, 98.0, 37.0, 0.2, "Blood Sugar must be between 0.5 and 50.0 mmol/L.")]
        public async Task Vitals_ExtremeOutOfRangeInputs_AreRejectedByControllerValidation(
            int systolic, int diastolic, int hr, int rr, double spo2, double temp, double bs, string expectedErrorMessage)
        {
            var context = await TestDbContextFactory.CreateSeededDbContextAsync();
            var appt = new Appointment
            {
                Id = 400,
                PatientId = 100,
                DoctorId = 10,
                AppointmentDatetime = DateTime.UtcNow.AddHours(1),
                EndTime = DateTime.UtcNow.AddHours(1).AddMinutes(15),
                Status = AppointmentStatus.Scheduled,
                ReasonForVisit = "Triage"
            };
            context.Appointments.Add(appt);
            await context.SaveChangesAsync();

            var controller = new VitalsController(context);
            ControllerTestHelper.SetupController(controller, TestPrincipalFactory.CreateAssistant(assistantId: 20, assignedDoctorId: 10));

            var vital = new PatientVital
            {
                AppointmentId = 400,
                PatientId = 100,
                SystolicBp = systolic,
                DiastolicBp = diastolic,
                HeartRate = hr,
                RespiratoryRate = rr,
                Spo2 = (decimal)spo2,
                Temperature = (decimal)temp,
                BloodSugar = (decimal)bs
            };

            var result = await controller.Create(vital);

            Assert.IsType<ViewResult>(result);
            Assert.False(controller.ModelState.IsValid);
            var errors = controller.ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
            Assert.Contains(expectedErrorMessage, errors);
        }

        [Fact]
        public async Task Vitals_ChildPatient_SavesVitalWithNullTriageScoreAndPriority()
        {
            var context = await TestDbContextFactory.CreateSeededDbContextAsync();

            // patientChild has Id: 101, IsChild = true
            var appt = new Appointment
            {
                Id = 401,
                PatientId = 101,
                DoctorId = 10,
                AppointmentDatetime = DateTime.UtcNow.AddHours(1),
                EndTime = DateTime.UtcNow.AddHours(1).AddMinutes(15),
                Status = AppointmentStatus.Scheduled,
                ReasonForVisit = "Pediatric Triage"
            };
            context.Appointments.Add(appt);
            await context.SaveChangesAsync();

            var controller = new VitalsController(context);
            ControllerTestHelper.SetupController(controller, TestPrincipalFactory.CreateAssistant(assistantId: 20, assignedDoctorId: 10));

            var vital = new PatientVital
            {
                AppointmentId = 401,
                PatientId = 101,
                SystolicBp = 100,
                DiastolicBp = 60,
                HeartRate = 95,
                RespiratoryRate = 22,
                Spo2 = 99.0m,
                Temperature = 37.0m
            };

            var result = await controller.Create(vital);

            var redirectResult = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Index", redirectResult.ActionName);
            Assert.Equal("Appointments", redirectResult.ControllerName);

            var saved = await context.PatientVitals.OrderByDescending(v => v.Id).FirstOrDefaultAsync();
            Assert.NotNull(saved);
            Assert.Null(saved.TriagePriority);
        }

        [Fact]
        public async Task Vitals_AdultPatient_SavesVitalWithCalculatedTriageScoreAndPriority()
        {
            var context = await TestDbContextFactory.CreateSeededDbContextAsync();

            // patient1 has Id: 100, IsChild = false
            var appt = new Appointment
            {
                Id = 402,
                PatientId = 100,
                DoctorId = 10,
                AppointmentDatetime = DateTime.UtcNow.AddHours(1),
                EndTime = DateTime.UtcNow.AddHours(1).AddMinutes(15),
                Status = AppointmentStatus.Scheduled,
                ReasonForVisit = "Adult Triage"
            };
            context.Appointments.Add(appt);
            await context.SaveChangesAsync();

            var controller = new VitalsController(context);
            ControllerTestHelper.SetupController(controller, TestPrincipalFactory.CreateAssistant(assistantId: 20, assignedDoctorId: 10));

            var vital = new PatientVital
            {
                AppointmentId = 402,
                PatientId = 100,
                SystolicBp = 120,
                DiastolicBp = 80,
                HeartRate = 70,
                RespiratoryRate = 16,
                Spo2 = 98.0m,
                Temperature = 37.0m,
                Consciousness = ConsciousnessLevel.Alert
            };

            var result = await controller.Create(vital);

            var redirectResult = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Index", redirectResult.ActionName);

            var saved = await context.PatientVitals.OrderByDescending(v => v.Id).FirstOrDefaultAsync();
            Assert.NotNull(saved);
            Assert.Equal(TriagePriority.Normal, saved.TriagePriority);
        }
    }
}
