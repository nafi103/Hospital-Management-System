using System;
using System.Linq;
using System.Threading.Tasks;
using HospitalManagementSystem.Controllers;
using HospitalManagementSystem.Models;
using HospitalManagementSystem.Tests.Fixtures;
using HospitalManagementSystem.Tests.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HospitalManagementSystem.Tests.Integration
{
    public class BillPaymentConcurrencyTests
    {
        private async Task<(ApplicationDbContext Context, Bill Bill)> SetupBillAsync(decimal subtotal, decimal discount = 0m)
        {
            var context = await TestDbContextFactory.CreateSeededDbContextAsync();
            var patient = await context.Patients.FirstAsync();

            var bill = new Bill
            {
                PatientId = patient.Id,
                SubtotalAmount = subtotal,
                DiscountAmount = discount,
                NetTotal = Math.Max(0, subtotal - discount),
                PaidAmount = 0m,
                Status = BillStatus.Unpaid,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            bill.BillItems.Add(new BillItem
            {
                Department = DepartmentType.Consultation,
                Description = "General Consultation",
                Amount = subtotal
            });

            context.Bills.Add(bill);
            await context.SaveChangesAsync();

            return (context, bill);
        }

        [Fact]
        public async Task Payment_ValidPartialPayment_AddsTransactionAndUpdatesPaidAmount()
        {
            var (context, bill) = await SetupBillAsync(subtotal: 1000m);
            var controller = ControllerTestHelper.SetupController(
                new BillsController(context),
                TestPrincipalFactory.CreateReceptionist(cashierId: 30));

            var result = await controller.Payment(bill.Id, PaymentAmount: 400m, paymentMethod: "bKash", notes: "Trx12345");

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal(nameof(BillsController.Details), redirect.ActionName);

            var updatedBill = await context.Bills
                .Include(b => b.PaymentTransactions)
                .FirstAsync(b => b.Id == bill.Id);

            Assert.Equal(400m, updatedBill.PaidAmount);
            Assert.Equal(BillStatus.PartiallyPaid, updatedBill.Status);

            var tx = Assert.Single(updatedBill.PaymentTransactions);
            Assert.Equal(400m, tx.Amount);
            Assert.Equal("bKash", tx.PaymentMethod);
            Assert.Equal("Trx12345", tx.Notes);
            Assert.Equal(30, tx.ProcessedById);
        }

        [Fact]
        public async Task Payment_MultiplePartialPayments_DerivesBalanceDynamicallyAndMarksPaid()
        {
            var (context, bill) = await SetupBillAsync(subtotal: 1000m);
            var controller = ControllerTestHelper.SetupController(
                new BillsController(context),
                TestPrincipalFactory.CreateReceptionist(cashierId: 30));

            // First partial payment: 600
            await controller.Payment(bill.Id, PaymentAmount: 600m, paymentMethod: "Cash");

            var billAfterFirst = await context.Bills.AsNoTracking().FirstAsync(b => b.Id == bill.Id);
            Assert.Equal(600m, billAfterFirst.PaidAmount);
            Assert.Equal(BillStatus.PartiallyPaid, billAfterFirst.Status);

            // Second payment: 400 (clearing balance)
            await controller.Payment(bill.Id, PaymentAmount: 400m, paymentMethod: "Card");

            var billAfterSecond = await context.Bills
                .Include(b => b.PaymentTransactions)
                .AsNoTracking()
                .FirstAsync(b => b.Id == bill.Id);

            Assert.Equal(1000m, billAfterSecond.PaidAmount);
            Assert.Equal(BillStatus.Paid, billAfterSecond.Status);
            Assert.Equal(2, billAfterSecond.PaymentTransactions.Count);
        }

        [Fact]
        public async Task Payment_ExceedingRemainingBalance_IsRejectedAndRollsBack()
        {
            var (context, bill) = await SetupBillAsync(subtotal: 500m);
            var controller = ControllerTestHelper.SetupController(
                new BillsController(context),
                TestPrincipalFactory.CreateReceptionist(cashierId: 30));

            // Attempt to pay 600 on a 500 bill
            var result = await controller.Payment(bill.Id, PaymentAmount: 600m, paymentMethod: "Cash");

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal(nameof(BillsController.Payment), redirect.ActionName);
            Assert.Contains("exceeds balance due", controller.TempData["ErrorMessage"]?.ToString());

            var unaffectedBill = await context.Bills
                .Include(b => b.PaymentTransactions)
                .AsNoTracking()
                .FirstAsync(b => b.Id == bill.Id);

            Assert.Equal(0m, unaffectedBill.PaidAmount);
            Assert.Equal(BillStatus.Unpaid, unaffectedBill.Status);
            Assert.Empty(unaffectedBill.PaymentTransactions);
        }

        [Fact]
        public async Task Payment_AttemptOnFullyPaidBill_IsBlocked()
        {
            var (context, bill) = await SetupBillAsync(subtotal: 500m);
            var controller = ControllerTestHelper.SetupController(
                new BillsController(context),
                TestPrincipalFactory.CreateReceptionist(cashierId: 30));

            // First pay in full
            await controller.Payment(bill.Id, PaymentAmount: 500m, paymentMethod: "Cash");

            // Attempt another payment on the already paid invoice
            var result = await controller.Payment(bill.Id, PaymentAmount: 100m, paymentMethod: "Cash");

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal(nameof(BillsController.Details), redirect.ActionName);
            Assert.Contains("already been fully paid", controller.TempData["ErrorMessage"]?.ToString());

            var txCount = await context.PaymentTransactions.CountAsync(t => t.BillId == bill.Id);
            Assert.Equal(1, txCount);
        }

        [Fact]
        public async Task Payment_ZeroOrNegativeAmount_IsRejectedImmediately()
        {
            var (context, bill) = await SetupBillAsync(subtotal: 500m);
            var controller = ControllerTestHelper.SetupController(
                new BillsController(context),
                TestPrincipalFactory.CreateReceptionist(cashierId: 30));

            var result = await controller.Payment(bill.Id, PaymentAmount: -50m);

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal(nameof(BillsController.Payment), redirect.ActionName);
            Assert.Contains("greater than zero", controller.TempData["ErrorMessage"]?.ToString());

            var txCount = await context.PaymentTransactions.CountAsync(t => t.BillId == bill.Id);
            Assert.Equal(0, txCount);
        }
    }
}
