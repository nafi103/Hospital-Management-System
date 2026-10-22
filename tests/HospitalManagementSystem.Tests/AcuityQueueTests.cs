using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HospitalManagementSystem.Hubs;
using HospitalManagementSystem.Models;
using HospitalManagementSystem.Services;
using HospitalManagementSystem.Tests.Helpers;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HospitalManagementSystem.Tests
{
    public class AcuityQueueTests
    {
        private readonly IHubContext<NotificationHub> _hubContext = new TestHubContext<NotificationHub>();

        [Fact]
        public void CalculatePriorityScore_AppliesAcuityAndAgingFormula()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            using var context = new ApplicationDbContext(options);
            var service = new AcuityQueueService(context, _hubContext);

            // NEWS2 = 5, MinutesWaited = 40
            // Score = (10.0 * 5) + (0.5 * 40) = 50 + 20 = 70.0
            double score = service.CalculatePriorityScore(5, 40);
            Assert.Equal(70.0, score);
        }

        [Fact]
        public void StarvationPrevention_AgedNormalPatientSurpassesNewArrivalWithMildAcuity()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            using var context = new ApplicationDbContext(options);
            var service = new AcuityQueueService(context, _hubContext);

            // Patient A: Normal acuity (NEWS2 = 0), has waited 80 minutes
            // Score A = (10 * 0) + (0.5 * 80) = 40.0
            double scoreA = service.CalculatePriorityScore(0, 80);

            // Patient B: Walk-in low acuity (NEWS2 = 3), just arrived (0 minutes)
            // Score B = (10 * 3) + (0.5 * 0) = 30.0
            double scoreB = service.CalculatePriorityScore(3, 0);

            // Patient A's aging term prevents indefinite starvation by walk-ins
            Assert.True(scoreA > scoreB);
        }

        [Fact]
        public void DeltaNews2_FlagsAcuteDeterioration_WhenScoreIncreasesByTwoOrMoreWithinTwelveHours()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            using var context = new ApplicationDbContext(options);
            var service = new AcuityQueueService(context, _hubContext);

            var now = DateTime.UtcNow;

            // Previous vital 4 hours ago: Normal parameters (Score ~ 0)
            var prevVital = new PatientVital
            {
                Id = 1,
                PatientId = 10,
                RespiratoryRate = 16,
                Spo2 = 98,
                OnSupplementalOxygen = false,
                Temperature = 37.0m,
                SystolicBp = 120,
                HeartRate = 72,
                Consciousness = ConsciousnessLevel.Alert,
                CreatedAt = now.AddHours(-4)
            };

            // Current vital: Tachypneic (RR 24 -> +2) and Tachycardic (HR 100 -> +1) (Score >= 3)
            var currVital = new PatientVital
            {
                Id = 2,
                PatientId = 10,
                RespiratoryRate = 24,
                Spo2 = 94,
                OnSupplementalOxygen = false,
                Temperature = 38.5m,
                SystolicBp = 120,
                HeartRate = 105,
                Consciousness = ConsciousnessLevel.Alert,
                CreatedAt = now
            };

            var delta = service.ComputeDeltaNews2(currVital, new[] { prevVital });

            Assert.True(delta.IsRapidDeterioration);
            Assert.True(delta.Delta >= 2);
            Assert.Contains("ACUTE PHYSIOLOGICAL DETERIORATION", delta.Message);
        }

        [Fact]
        public void DeltaNews2_ReportsStable_WhenScoreChangeIsBelowTwo()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            using var context = new ApplicationDbContext(options);
            var service = new AcuityQueueService(context, _hubContext);

            var now = DateTime.UtcNow;
            var prevVital = new PatientVital
            {
                Id = 1,
                PatientId = 10,
                RespiratoryRate = 16,
                Spo2 = 98,
                OnSupplementalOxygen = false,
                Temperature = 37.0m,
                SystolicBp = 120,
                HeartRate = 72,
                Consciousness = ConsciousnessLevel.Alert,
                CreatedAt = now.AddHours(-2)
            };

            var currVital = new PatientVital
            {
                Id = 2,
                PatientId = 10,
                RespiratoryRate = 18,
                Spo2 = 97,
                OnSupplementalOxygen = false,
                Temperature = 37.2m,
                SystolicBp = 122,
                HeartRate = 76,
                Consciousness = ConsciousnessLevel.Alert,
                CreatedAt = now
            };

            var delta = service.ComputeDeltaNews2(currVital, new[] { prevVital });

            Assert.False(delta.IsRapidDeterioration);
            Assert.Contains("stable", delta.Message, StringComparison.OrdinalIgnoreCase);
        }
    }
}
