using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HospitalManagementSystem.Models;

namespace HospitalManagementSystem.Services
{
    public record AcuityQueueItem(
        int AppointmentId,
        int PatientId,
        string PatientName,
        string Uhid,
        int DoctorId,
        DateTime AppointmentTimeUtc,
        int News2Score,
        TriagePriority? TriagePriority,
        double MinutesWaited,
        double PriorityScore,
        int QueueRank,
        int EstimatedWaitMinutes);

    public record DeltaNews2Result(
        int CurrentScore,
        int? PreviousScore,
        int Delta,
        double HoursBetween,
        bool IsRapidDeterioration,
        string Message);

    public interface IAcuityQueueService
    {
        double CalculatePriorityScore(int news2Score, double minutesWaited);
        Task<List<AcuityQueueItem>> GetDoctorQueueAsync(int doctorId, CancellationToken ct = default);
        Task BroadcastQueueUpdateAsync(int doctorId, CancellationToken ct = default);
        DeltaNews2Result ComputeDeltaNews2(PatientVital currentVital, IEnumerable<PatientVital> pastVitals);
    }
}
