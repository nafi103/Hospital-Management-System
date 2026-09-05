using System;
using System.Collections.Generic;

namespace HospitalManagementSystem.Models.ViewModels
{
    public record DepartmentRevenue(DepartmentType Department, decimal Amount);

    public record BedOccupancyByCategory(BedCategory Category, int Total, int Occupied);

    public record AppointmentVolumePoint(DateTime Date, int Scheduled, int Completed, int Cancelled, int InConsultation)
    {
        public int Total => Scheduled + Completed + Cancelled + InConsultation;
    }

    public record TriageCount(TriagePriority Priority, int Count);

    public record AiVerdictCount(AiSuggestionVerdict Verdict, int Count);

    public record TopMedicine(string Name, int TotalQuantity);

    // The reporting payload for the admin analytics screen. A typed view model rather
    // than ViewBag entries because the shape here - six panels, each its own query
    // result - is too large to track reliably through loosely-typed dictionary keys.
    public class ReportsViewModel
    {
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }

        // Revenue and appointment volume respect the from/to filter; the remaining
        // panels (occupancy, triage, AI governance, top medicines) are current-state
        // snapshots and are always computed over all data.
        public decimal TotalBilled { get; set; }
        public decimal TotalCollected { get; set; }
        public decimal TotalOutstanding { get; set; }
        public List<DepartmentRevenue> RevenueByDepartment { get; set; } = new();

        public List<BedOccupancyByCategory> BedOccupancy { get; set; } = new();

        public List<AppointmentVolumePoint> AppointmentVolume { get; set; } = new();

        public List<TriageCount> TriageMix { get; set; } = new();

        public List<AiVerdictCount> AiVerdicts { get; set; } = new();
        public int AiTotalInputTokens { get; set; }
        public int AiTotalOutputTokens { get; set; }
        public double AiAverageLatencyMs { get; set; }

        public List<TopMedicine> TopMedicines { get; set; } = new();
    }
}
