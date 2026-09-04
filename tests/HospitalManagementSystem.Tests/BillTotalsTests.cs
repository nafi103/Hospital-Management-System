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
    public void RecalculateTotals_FullyDiscountedBill_CurrentlyStaysUnpaidDespiteZeroBalance()
    {
        // Known limitation: RecalculateTotals guards Paid with "NetTotal > 0", so a
        // fully-discounted bill (NetTotal == 0, nothing owed) can never reach Paid -
        // it stays Unpaid forever even though the patient owes nothing. Documented
        // here rather than silently fixed; see docs/DEFENSE-NOTES.md.
        var bill = MakeBill(discount: 200m, paid: 0m, itemAmounts: [200m]);
        bill.RecalculateTotals();

        Assert.Equal(0m, bill.NetTotal);
        Assert.Equal(BillStatus.Unpaid, bill.Status);
    }
}
