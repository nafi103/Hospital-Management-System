using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using HospitalManagementSystem.Hubs;
using HospitalManagementSystem.Models;

namespace HospitalManagementSystem.Services
{
    public class AcuityQueueService : IAcuityQueueService
    {
        private readonly ApplicationDbContext _context;
        private readonly IHubContext<NotificationHub> _hubContext;

        public const double AcuityWeight = 10.0;
        public const double WaitWeight = 0.5;
        public const int SlotDurationMinutes = 15;

        public AcuityQueueService(ApplicationDbContext context, IHubContext<NotificationHub> hubContext)
        {
            _context = context;
            _hubContext = hubContext;
        }

        public double CalculatePriorityScore(int news2Score, double minutesWaited)
        {
            return (AcuityWeight * Math.Max(0, news2Score)) + (WaitWeight * Math.Max(0, minutesWaited));
        }

        public async Task<List<AcuityQueueItem>> GetDoctorQueueAsync(int doctorId, CancellationToken ct = default)
        {
            var todayLocal = HospitalClock.Today;
            var startUtc = HospitalClock.GetStartOfDayUtc(todayLocal);
            var endUtc = HospitalClock.GetEndOfDayUtc(todayLocal);

            var appointments = await _context.Appointments
                .Include(a => a.Patient)
                .Where(a => a.DoctorId == doctorId
                         && a.AppointmentDatetime >= startUtc
                         && a.AppointmentDatetime < endUtc
                         && a.Status != AppointmentStatus.Cancelled
                         && a.Status != AppointmentStatus.Completed)
                .ToListAsync(ct);

            var appointmentIds = appointments.Select(a => a.Id).ToList();

            var vitals = await _context.PatientVitals
                .Where(v => v.AppointmentId.HasValue && appointmentIds.Contains(v.AppointmentId.Value))
                .OrderByDescending(v => v.CreatedAt)
                .ToListAsync(ct);

            var latestVitalByAppt = vitals
                .GroupBy(v => v.AppointmentId!.Value)
                .ToDictionary(g => g.Key, g => g.First());

            var nowUtc = DateTime.UtcNow;
            var unranked = new List<(Appointment Appt, int News2, TriagePriority? Priority, double MinutesWaited, double Score)>();

            foreach (var appt in appointments)
            {
                int news2 = 0;
                TriagePriority? priority = null;

                if (latestVitalByAppt.TryGetValue(appt.Id, out var vital))
                {
                    priority = vital.TriagePriority;
                    if (appt.Patient?.IsChild != true)
                    {
                        var calc = News2Calculator.Calculate(
                            vital.RespiratoryRate, vital.Spo2, vital.OnSupplementalOxygen,
                            vital.Temperature, vital.SystolicBp, vital.HeartRate, vital.Consciousness);
                        news2 = calc.Score;
                    }
                }

                double minutesWaited = Math.Max(0, (nowUtc - appt.AppointmentDatetime).TotalMinutes);
                double score = CalculatePriorityScore(news2, minutesWaited);

                unranked.Add((appt, news2, priority, minutesWaited, score));
            }

            // High priority score first. In case of ties, oldest appointment time first.
            var ordered = unranked
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Appt.AppointmentDatetime)
                .ToList();

            var result = new List<AcuityQueueItem>();
            for (int i = 0; i < ordered.Count; i++)
            {
                var item = ordered[i];
                int rank = i + 1;
                int ewt = (rank - 1) * SlotDurationMinutes;

                result.Add(new AcuityQueueItem(
                    item.Appt.Id,
                    item.Appt.PatientId,
                    item.Appt.Patient?.FullName ?? "Unknown",
                    item.Appt.Patient?.Uhid ?? "",
                    item.Appt.DoctorId,
                    item.Appt.AppointmentDatetime,
                    item.News2,
                    item.Priority,
                    item.MinutesWaited,
                    item.Score,
                    rank,
                    ewt));
            }

            return result;
        }

        public async Task BroadcastQueueUpdateAsync(int doctorId, CancellationToken ct = default)
        {
            var queue = await GetDoctorQueueAsync(doctorId, ct);

            // Broadcast to the doctor's channel and assistant channel
            await _hubContext.Clients.Group($"Doctor_{doctorId}").SendAsync(
                "QueueOrderUpdated",
                new
                {
                    DoctorId = doctorId,
                    Timestamp = DateTime.UtcNow,
                    TotalWaiting = queue.Count,
                    Queue = queue
                },
                ct);

            // Broadcast estimated wait times for individual patient tracking
            foreach (var item in queue)
            {
                await _hubContext.Clients.Group($"Doctor_{doctorId}").SendAsync(
                    "PatientWaitTimeUpdated",
                    new
                    {
                        AppointmentId = item.AppointmentId,
                        PatientId = item.PatientId,
                        QueueRank = item.QueueRank,
                        EstimatedWaitMinutes = item.EstimatedWaitMinutes
                    },
                    ct);
            }
        }

        public DeltaNews2Result ComputeDeltaNews2(PatientVital currentVital, IEnumerable<PatientVital> pastVitals)
        {
            var currentScore = News2Calculator.Calculate(
                currentVital.RespiratoryRate, currentVital.Spo2, currentVital.OnSupplementalOxygen,
                currentVital.Temperature, currentVital.SystolicBp, currentVital.HeartRate, currentVital.Consciousness).Score;

            var cutoff = currentVital.CreatedAt.AddHours(-12);
            var previous = pastVitals
                .Where(v => v.Id != currentVital.Id && v.CreatedAt >= cutoff && v.CreatedAt <= currentVital.CreatedAt)
                .OrderByDescending(v => v.CreatedAt)
                .FirstOrDefault();

            if (previous == null)
            {
                return new DeltaNews2Result(
                    currentScore,
                    null,
                    0,
                    0,
                    false,
                    $"Baseline observation established: NEWS2 score {currentScore}.");
            }

            var prevScore = News2Calculator.Calculate(
                previous.RespiratoryRate, previous.Spo2, previous.OnSupplementalOxygen,
                previous.Temperature, previous.SystolicBp, previous.HeartRate, previous.Consciousness).Score;

            int delta = currentScore - prevScore;
            double hours = (currentVital.CreatedAt - previous.CreatedAt).TotalHours;
            bool isDeteriorating = delta >= 2;

            string message = isDeteriorating
                ? $"ACUTE PHYSIOLOGICAL DETERIORATION: NEWS2 increased by +{delta} points in {hours:0.1} hours (Score: {prevScore} -> {currentScore}). Immediate clinical review indicated."
                : $"Longitudinal trend: NEWS2 delta {delta:+#;-#;0} over {hours:0.1} hours (Score: {prevScore} -> {currentScore}). Physiological parameters stable.";

            return new DeltaNews2Result(
                currentScore,
                prevScore,
                delta,
                hours,
                isDeteriorating,
                message);
        }
    }
}
