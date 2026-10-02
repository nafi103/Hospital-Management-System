using HospitalManagementSystem.Models;

namespace HospitalManagementSystem.Tests;

public class BillTotalsTests
{
    private static Bill MakeBill(decimal discount = 0m, decimal paid = 0m, params decimal[] itemAmounts)
    {
        var bill = new Bill
        {
            DiscountAmount = discount,
            PaidAmount = paid,
            BillItems = itemAmounts.Select(a => new BillItem
            {
                Department = DepartmentType.Consultation,
                Description = "Item",
                Amount = a
            }).ToList()
        };
        return bill;
    }

    [Fact]
    public void RecalculateTotals_SumsItemAmountsIntoSubtotal()
    {
        var bill = MakeBill(itemAmounts: [100m, 250m, 50m]);
        bill.RecalculateTotals();
        Assert.Equal(400m, bill.SubtotalAmount);
    }

    [Fact]
    public void RecalculateTotals_EmptyItemList_YieldsZeroSubtotal()
    {
        var bill = MakeBill();
        bill.RecalculateTotals();
        Assert.Equal(0m, bill.SubtotalAmount);
        Assert.Equal(0m, bill.NetTotal);
    }

    [Fact]
    public void RecalculateTotals_NetTotalIsSubtotalMinusDiscount()
    {
        var bill = MakeBill(discount: 50m, itemAmounts: [300m]);
        bill.RecalculateTotals();
        Assert.Equal(250m, bill.NetTotal);
    }

    [Fact]
    public void RecalculateTotals_NoPayment_IsUnpaid()
    {
        var bill = MakeBill(paid: 0m, itemAmounts: [200m]);
        bill.RecalculateTotals();
        Assert.Equal(BillStatus.Unpaid, bill.Status);
    }

    [Fact]
    public void RecalculateTotals_PartialPayment_IsPartiallyPaid()
    {
        var bill = MakeBill(paid: 100m, itemAmounts: [200m]);
        bill.RecalculateTotals();
        Assert.Equal(BillStatus.PartiallyPaid, bill.Status);
    }

    [Fact]
    public void RecalculateTotals_ExactPayment_IsPaid()
    {
        var bill = MakeBill(paid: 200m, itemAmounts: [200m]);
        bill.RecalculateTotals();
        Assert.Equal(BillStatus.Paid, bill.Status);
    }

    [Fact]
    public void RecalculateTotals_Overpayment_IsPaid()
    {
        var bill = MakeBill(paid: 250m, itemAmounts: [200m]);
        bill.RecalculateTotals();
        Assert.Equal(BillStatus.Paid, bill.Status);
    }

    [Fact]
    public void RecalculateTotals_FullyDiscountedBill_TransitionsToPaid()
    {
        var bill = MakeBill(discount: 200m, paid: 0m, itemAmounts: [200m]);
        bill.RecalculateTotals();

        Assert.Equal(0m, bill.NetTotal);
        Assert.Equal(BillStatus.Paid, bill.Status);
    }

    [Fact]
    public void RecalculateTotals_DiscountExceedingSubtotal_ClampsToZeroAndTransitionsToPaid()
    {
        var bill = MakeBill(discount: 250m, paid: 0m, itemAmounts: [200m]);
        bill.RecalculateTotals();

        Assert.Equal(0m, bill.NetTotal);
        Assert.Equal(BillStatus.Paid, bill.Status);
    }

    [Fact]
    public void RecalculateTotals_MultiTransactionLedger_ClearsBalanceAndTransitionsToPaid()
    {
        var bill = MakeBill(itemAmounts: [1000m, 500m]);
        bill.PaymentTransactions.Add(new PaymentTransaction { Amount = 500m, PaymentMethod = "Cash", TransactionDate = DateTime.UtcNow });
        bill.PaymentTransactions.Add(new PaymentTransaction { Amount = 1000m, PaymentMethod = "Card", TransactionDate = DateTime.UtcNow });

        bill.PaidAmount = bill.PaymentTransactions.Sum(pt => pt.Amount);
        bill.RecalculateTotals();

        Assert.Equal(1500m, bill.SubtotalAmount);
        Assert.Equal(1500m, bill.NetTotal);
        Assert.Equal(1500m, bill.PaidAmount);
        Assert.Equal(BillStatus.Paid, bill.Status);
    }

    [Fact]
    public void RecalculateTotals_MultiTransactionLedger_PartialPayment_RemainsPartiallyPaid()
    {
        var bill = MakeBill(itemAmounts: [1500m]);
        bill.PaymentTransactions.Add(new PaymentTransaction { Amount = 600m, PaymentMethod = "Cash", TransactionDate = DateTime.UtcNow });

        bill.PaidAmount = bill.PaymentTransactions.Sum(pt => pt.Amount);
        bill.RecalculateTotals();

        Assert.Equal(1500m, bill.NetTotal);
        Assert.Equal(600m, bill.PaidAmount);
        Assert.Equal(BillStatus.PartiallyPaid, bill.Status);
    }

    [Fact]
    public void RecalculateTotals_DiscountWithPartialPayment_DerivesRemainingBalanceCorrectly()
    {
        var bill = MakeBill(discount: 300m, itemAmounts: [1000m]);
        bill.PaymentTransactions.Add(new PaymentTransaction { Amount = 350m, PaymentMethod = "Cash", TransactionDate = DateTime.UtcNow });

        bill.PaidAmount = bill.PaymentTransactions.Sum(pt => pt.Amount);
        bill.RecalculateTotals();

        Assert.Equal(700m, bill.NetTotal);
        Assert.Equal(350m, bill.PaidAmount);
        var remainingBalance = bill.NetTotal - bill.PaidAmount;
        Assert.Equal(350m, remainingBalance);
        Assert.Equal(BillStatus.PartiallyPaid, bill.Status);
    }
}
