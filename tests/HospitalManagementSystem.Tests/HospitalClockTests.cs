using System;
using HospitalManagementSystem.Services;
using Xunit;

namespace HospitalManagementSystem.Tests;

public class HospitalClockTests
{
    [Fact]
    public void TimeZone_IsConfiguredWithPlusSixOffset()
    {
        // Bangladesh Standard Time is UTC+6 with no daylight saving time transitions
        var baseUtc = new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        var offset = HospitalClock.TimeZone.GetUtcOffset(baseUtc);
        Assert.Equal(TimeSpan.FromHours(6), offset);
    }

    [Fact]
    public void GetStartOfDayUtc_ConvertsLocalDateToUtcStart()
    {
        var localDate = new DateTime(2026, 10, 1);
        var startUtc = HospitalClock.GetStartOfDayUtc(localDate);

        // 2026-10-01 00:00:00 +06:00 is 2026-09-30 18:00:00 UTC
        Assert.Equal(DateTimeKind.Utc, startUtc.Kind);
        Assert.Equal(new DateTime(2026, 9, 30, 18, 0, 0, DateTimeKind.Utc), startUtc);
    }

    [Fact]
    public void GetEndOfDayUtc_ReturnsExactlyTwentyFourHoursAfterStart()
    {
        var localDate = new DateTime(2026, 10, 1);
        var startUtc = HospitalClock.GetStartOfDayUtc(localDate);
        var endUtc = HospitalClock.GetEndOfDayUtc(localDate);

        Assert.Equal(DateTimeKind.Utc, endUtc.Kind);
        Assert.Equal(new DateTime(2026, 10, 1, 18, 0, 0, DateTimeKind.Utc), endUtc);
        Assert.Equal(TimeSpan.FromHours(24), endUtc - startUtc);
    }

    [Fact]
    public void ToHospitalLocal_ConvertsUtcToLocalTimeAccurately()
    {
        // 4:00 AM UTC on Oct 1 -> 10:00 AM Dhaka on Oct 1
        var utc = new DateTime(2026, 10, 1, 4, 0, 0, DateTimeKind.Utc);
        var local = HospitalClock.ToHospitalLocal(utc);
        Assert.Equal(new DateTime(2026, 10, 1, 10, 0, 0), local);

        // 7:00 PM UTC on Oct 1 -> 1:00 AM Dhaka on Oct 2
        var lateUtc = new DateTime(2026, 10, 1, 19, 0, 0, DateTimeKind.Utc);
        var lateLocal = HospitalClock.ToHospitalLocal(lateUtc);
        Assert.Equal(new DateTime(2026, 10, 2, 1, 0, 0), lateLocal);
    }

    [Fact]
    public void ToHospitalTime_ExtensionMethod_MatchesServiceResult()
    {
        var utc = new DateTime(2026, 10, 1, 8, 30, 0, DateTimeKind.Utc);
        Assert.Equal(HospitalClock.ToHospitalLocal(utc), utc.ToHospitalTime());
    }
}
