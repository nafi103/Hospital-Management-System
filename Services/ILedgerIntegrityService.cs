using System;
using System.Threading;
using System.Threading.Tasks;
using HospitalManagementSystem.Models;

namespace HospitalManagementSystem.Services
{
    public class LedgerIntegrityReport
    {
        public bool IsValid { get; set; } = true;
        public int TotalTransactions { get; set; }
        public int VerifiedTransactions { get; set; }
        public decimal TotalLedgerAmount { get; set; }
        public DateTime? LastVerifiedAt { get; set; }
        public string? TamperedTransactionId { get; set; }
        public int? TamperedRowId { get; set; }
        public string? FailureReason { get; set; }
        public string? LatestHash { get; set; }
    }

    public interface ILedgerIntegrityService
    {
        Task<LedgerIntegrityReport> VerifyLedgerAsync(CancellationToken ct = default);
        Task StampTransactionAsync(PaymentTransaction transaction, CancellationToken ct = default);
    }
}
