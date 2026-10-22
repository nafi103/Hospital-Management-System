using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using HospitalManagementSystem.Models;

namespace HospitalManagementSystem.Services
{
    public class LedgerIntegrityService : ILedgerIntegrityService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<LedgerIntegrityService> _logger;

        public const string GenesisHash = "0000000000000000000000000000000000000000000000000000000000000000";

        public LedgerIntegrityService(ApplicationDbContext context, ILogger<LedgerIntegrityService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task StampTransactionAsync(PaymentTransaction transaction, CancellationToken ct = default)
        {
            // Find latest recorded transaction in the chain
            var latest = await _context.PaymentTransactions
                .OrderByDescending(t => t.Id)
                .Select(t => new { t.Id, t.RowHash })
                .FirstOrDefaultAsync(ct);

            var prevHash = latest?.RowHash ?? GenesisHash;
            transaction.PreviousHash = prevHash;
            transaction.RowHash = transaction.ComputeCurrentHash(prevHash);
        }

        public async Task<LedgerIntegrityReport> VerifyLedgerAsync(CancellationToken ct = default)
        {
            var transactions = await _context.PaymentTransactions
                .AsNoTracking()
                .OrderBy(t => t.Id)
                .ToListAsync(ct);

            var report = new LedgerIntegrityReport
            {
                TotalTransactions = transactions.Count,
                LastVerifiedAt = DateTime.UtcNow,
                TotalLedgerAmount = transactions.Sum(t => t.Amount)
            };

            if (transactions.Count == 0)
            {
                report.LatestHash = GenesisHash;
                return report;
            }

            string expectedPrevHash = GenesisHash;

            foreach (var tx in transactions)
            {
                // 1. Verify link continuity with previous block
                var actualPrev = tx.PreviousHash ?? GenesisHash;
                if (!string.Equals(actualPrev, expectedPrevHash, StringComparison.OrdinalIgnoreCase))
                {
                    report.IsValid = false;
                    report.TamperedRowId = tx.Id;
                    report.TamperedTransactionId = tx.TransactionId ?? $"Row#{tx.Id}";
                    report.FailureReason = $"Broken chain link at Transaction #{tx.Id}: PreviousHash mismatch. Expected '{expectedPrevHash}', found '{actualPrev}'.";
                    _logger.LogWarning("Ledger integrity failure: {Reason}", report.FailureReason);
                    return report;
                }

                // 2. Recompute row hash from values
                var computedHash = tx.ComputeCurrentHash(expectedPrevHash);
                if (!string.Equals(computedHash, tx.RowHash, StringComparison.OrdinalIgnoreCase))
                {
                    report.IsValid = false;
                    report.TamperedRowId = tx.Id;
                    report.TamperedTransactionId = tx.TransactionId ?? $"Row#{tx.Id}";
                    report.FailureReason = $"Data tamper detected at Transaction #{tx.Id} (Amount ৳{tx.Amount:0.00}): RowHash mismatch. Row contents have been altered directly in the database!";
                    _logger.LogWarning("Ledger integrity failure: {Reason}", report.FailureReason);
                    return report;
                }

                expectedPrevHash = tx.RowHash!;
                report.VerifiedTransactions++;
            }

            report.LatestHash = expectedPrevHash;
            return report;
        }
    }
}
