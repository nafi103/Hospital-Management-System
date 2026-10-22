using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using HospitalManagementSystem.Models;
using HospitalManagementSystem.Services;
using HospitalManagementSystem.Tests.Fixtures;
using Xunit;

namespace HospitalManagementSystem.Tests
{
    public class LedgerIntegrityTests
    {
        private async Task<(ApplicationDbContext Context, LedgerIntegrityService Service, Bill Bill)> SetupAsync()
        {
            var context = await TestDbContextFactory.CreateSeededDbContextAsync();
            var patient = await context.Patients.FirstAsync();
            var bill = new Bill
            {
                PatientId = patient.Id,
                SubtotalAmount = 1000m,
                NetTotal = 1000m,
                Status = BillStatus.Unpaid,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            bill.BillItems.Add(new BillItem { Department = DepartmentType.Consultation, Description = "Consultation", Amount = 1000m });
            context.Bills.Add(bill);
            await context.SaveChangesAsync();

            var service = new LedgerIntegrityService(context, NullLogger<LedgerIntegrityService>.Instance);
            return (context, service, bill);
        }

        [Fact]
        public async Task VerifyLedger_ValidChain_ReturnsValidReport()
        {
            var (context, service, bill) = await SetupAsync();

            var tx1 = new PaymentTransaction
            {
                BillId = bill.Id,
                Amount = 300m,
                PaymentMethod = "Cash",
                TransactionId = "TX-001",
                TransactionDate = DateTime.UtcNow.AddMinutes(-10)
            };
            await service.StampTransactionAsync(tx1);
            context.PaymentTransactions.Add(tx1);
            await context.SaveChangesAsync();

            var tx2 = new PaymentTransaction
            {
                BillId = bill.Id,
                Amount = 700m,
                PaymentMethod = "bKash",
                TransactionId = "TX-002",
                TransactionDate = DateTime.UtcNow.AddMinutes(-5)
            };
            await service.StampTransactionAsync(tx2);
            context.PaymentTransactions.Add(tx2);
            await context.SaveChangesAsync();

            Assert.Equal(tx1.RowHash, tx2.PreviousHash);

            var report = await service.VerifyLedgerAsync();

            Assert.True(report.IsValid);
            Assert.True(report.VerifiedTransactions >= 2);
            Assert.Null(report.TamperedRowId);
            Assert.Null(report.FailureReason);
        }

        [Fact]
        public async Task VerifyLedger_DirectDatabaseTamper_DetectsTamperAndFlagsExactRow()
        {
            var (context, service, bill) = await SetupAsync();

            var tx1 = new PaymentTransaction
            {
                BillId = bill.Id,
                Amount = 500m,
                PaymentMethod = "Cash",
                TransactionId = "TX-ORIGINAL",
                TransactionDate = DateTime.UtcNow.AddMinutes(-10)
            };
            await service.StampTransactionAsync(tx1);
            context.PaymentTransactions.Add(tx1);
            await context.SaveChangesAsync();

            // Simulate database administrator directly altering Amount in SQL table without recalculating RowHash
            tx1.Amount = 100m; // Fraudulent alteration!
            await context.SaveChangesAsync();

            var report = await service.VerifyLedgerAsync();

            Assert.False(report.IsValid);
            Assert.Equal(tx1.Id, report.TamperedRowId);
            Assert.Contains("Data tamper detected", report.FailureReason);
        }

        [Fact]
        public async Task VerifyLedger_BrokenPreviousHash_DetectsBrokenChainLink()
        {
            var (context, service, bill) = await SetupAsync();

            var tx1 = new PaymentTransaction
            {
                BillId = bill.Id,
                Amount = 400m,
                PaymentMethod = "Cash",
                TransactionId = "TX-LINK-1",
                TransactionDate = DateTime.UtcNow.AddMinutes(-10)
            };
            await service.StampTransactionAsync(tx1);
            context.PaymentTransactions.Add(tx1);
            await context.SaveChangesAsync();

            var tx2 = new PaymentTransaction
            {
                BillId = bill.Id,
                Amount = 600m,
                PaymentMethod = "Card",
                TransactionId = "TX-LINK-2",
                TransactionDate = DateTime.UtcNow.AddMinutes(-5)
            };
            await service.StampTransactionAsync(tx2);
            // Simulate broken chain link
            tx2.PreviousHash = "corrupted_fake_previous_hash";
            context.PaymentTransactions.Add(tx2);
            await context.SaveChangesAsync();

            var report = await service.VerifyLedgerAsync();

            Assert.False(report.IsValid);
            Assert.Equal(tx2.Id, report.TamperedRowId);
            Assert.Contains("Broken chain link", report.FailureReason);
        }
    }
}
