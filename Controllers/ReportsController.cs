using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HospitalManagementSystem.Models;
using HospitalManagementSystem.Models.ViewModels;

namespace HospitalManagementSystem.Controllers
{
    // Admin-only analytics over data every other controller already writes: revenue,
    // bed occupancy, appointment volume, triage mix, and AI governance. Nothing here
    // writes to the database - every action is a read-only aggregate.
    [Authorize(Roles = "Admin")]
    public class ReportsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ReportsController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: Reports
        public async Task<IActionResult> Index(DateTime? from, DateTime? to)
        {
            var model = await BuildModelAsync(from, to);
            return View(model);
        }

        // GET: Reports/ExportAppointmentsCsv?from=...&to=...
        public async Task<IActionResult> ExportAppointmentsCsv(DateTime? from, DateTime? to)
        {
            var model = await BuildModelAsync(from, to);

            var csv = new StringBuilder();
            csv.AppendLine("Date,Scheduled,Completed,Cancelled,InConsultation,Total");
            foreach (var point in model.AppointmentVolume)
            {
                csv.AppendLine($"{point.Date:yyyy-MM-dd},{point.Scheduled},{point.Completed},{point.Cancelled},{point.InConsultation},{point.Total}");
            }

            var bytes = Encoding.UTF8.GetBytes(csv.ToString());
            var fileName = $"appointment-volume_{model.FromDate:yyyyMMdd}-{model.ToDate:yyyyMMdd}.csv";
            return File(bytes, "text/csv", fileName);
        }

        private async Task<ReportsViewModel> BuildModelAsync(DateTime? from, DateTime? to)
        {
            var toDate = (to ?? DateTime.UtcNow.Date).Date;
            var fromDate = (from ?? toDate.AddDays(-13)).Date;
            // Inclusive of the whole "to" day.
            var toDateExclusive = toDate.AddDays(1);

            var model = new ReportsViewModel { FromDate = fromDate, ToDate = toDate };

            var billsInRange = await _context.Bills
                .Where(b => b.CreatedAt >= fromDate && b.CreatedAt < toDateExclusive)
                .ToListAsync();
            model.TotalBilled = billsInRange.Sum(b => b.NetTotal);
            model.TotalCollected = billsInRange.Sum(b => b.PaidAmount);
            model.TotalOutstanding = model.TotalBilled - model.TotalCollected;

            var revenueByDepartment = from bi in _context.BillItems
                                       join b in _context.Bills on bi.BillId equals b.Id
                                       where b.CreatedAt >= fromDate && b.CreatedAt < toDateExclusive
                                       group bi by bi.Department into g
                                       select new { Department = g.Key, Amount = g.Sum(x => x.Amount) };
            model.RevenueByDepartment = (await revenueByDepartment.ToListAsync())
                .Select(x => new DepartmentRevenue(x.Department, x.Amount))
                .OrderByDescending(x => x.Amount)
                .ToList();

            // Bed occupancy is always computed from BedTransfers, never from the
            // [NotMapped] Bed.Status - that property can't be translated to SQL and
            // would force the whole Beds table to load client-side to evaluate it.
            var totalByCategory = await _context.Beds
                .GroupBy(b => b.Category)
                .Select(g => new { Category = g.Key, Total = g.Count() })
                .ToListAsync();
            var occupiedByCategory = (await _context.BedTransfers
                    .Where(bt => bt.EndDate == null)
                    .Select(bt => bt.Bed.Category)
                    .ToListAsync())
                .GroupBy(c => c)
                .ToDictionary(g => g.Key, g => g.Count());
            model.BedOccupancy = totalByCategory
                .Select(t => new BedOccupancyByCategory(t.Category, t.Total, occupiedByCategory.GetValueOrDefault(t.Category)))
                .ToList();

            // Timestamptz truncation to a plain date isn't reliably translatable across
            // providers, so the date/status pair is projected narrowly first and grouped
            // into daily buckets in memory - at demo scale (dozens of rows) this is a
            // single round trip either way.
            var appointmentsInRange = await _context.Appointments
                .Where(a => a.AppointmentDatetime >= fromDate && a.AppointmentDatetime < toDateExclusive)
                .Select(a => new { Date = a.AppointmentDatetime.Date, a.Status })
                .ToListAsync();
            var appointmentsByDate = appointmentsInRange.GroupBy(a => a.Date).ToDictionary(g => g.Key, g => g.ToList());

            for (var day = fromDate; day <= toDate; day = day.AddDays(1))
            {
                var dayItems = appointmentsByDate.GetValueOrDefault(day);
                model.AppointmentVolume.Add(new AppointmentVolumePoint(
                    day,
                    dayItems?.Count(x => x.Status == AppointmentStatus.Scheduled) ?? 0,
                    dayItems?.Count(x => x.Status == AppointmentStatus.Completed) ?? 0,
                    dayItems?.Count(x => x.Status == AppointmentStatus.Cancelled) ?? 0,
                    dayItems?.Count(x => x.Status == AppointmentStatus.InConsultation) ?? 0));
            }

            model.TriageMix = (await _context.PatientVitals
                    .Where(v => v.TriagePriority != null)
                    .GroupBy(v => v.TriagePriority)
                    .Select(g => new { Priority = g.Key!.Value, Count = g.Count() })
                    .ToListAsync())
                .Select(x => new TriageCount(x.Priority, x.Count))
                .OrderBy(x => x.Priority)
                .ToList();

            model.AiVerdicts = (await _context.AiSuggestions
                    .GroupBy(s => s.Verdict)
                    .Select(g => new { Verdict = g.Key, Count = g.Count() })
                    .ToListAsync())
                .Select(x => new AiVerdictCount(x.Verdict, x.Count))
                .OrderBy(x => x.Verdict)
                .ToList();

            var aiTotals = await _context.AiSuggestions
                .GroupBy(s => 1)
                .Select(g => new
                {
                    TotalInput = g.Sum(s => s.InputTokens),
                    TotalOutput = g.Sum(s => s.OutputTokens),
                    AvgLatency = g.Average(s => (double)s.LatencyMs)
                })
                .FirstOrDefaultAsync();
            if (aiTotals != null)
            {
                model.AiTotalInputTokens = aiTotals.TotalInput;
                model.AiTotalOutputTokens = aiTotals.TotalOutput;
                model.AiAverageLatencyMs = aiTotals.AvgLatency;
            }

            model.TopMedicines = (await _context.PrescriptionItems
                    .Where(pi => pi.Medicine != null)
                    .GroupBy(pi => pi.Medicine!.Name)
                    .Select(g => new { Name = g.Key, TotalQuantity = g.Sum(pi => pi.Quantity) })
                    .OrderByDescending(x => x.TotalQuantity)
                    .Take(5)
                    .ToListAsync())
                .Select(x => new TopMedicine(x.Name, x.TotalQuantity))
                .ToList();

            return model;
        }
    }
}
