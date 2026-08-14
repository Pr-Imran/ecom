using FashionStore.Application.Common;
using Xunit;

namespace FashionStore.UnitTests.Reports;

public class ReportDateRangeHelperTests
{
    [Fact]
    public void ResolveTimeZone_UtcAliases_ReturnUtc()
    {
        Assert.Same(TimeZoneInfo.Utc, ReportDateRangeHelper.ResolveTimeZone("UTC"));
        Assert.Same(TimeZoneInfo.Utc, ReportDateRangeHelper.ResolveTimeZone("Etc/UTC"));
        Assert.Same(TimeZoneInfo.Utc, ReportDateRangeHelper.ResolveTimeZone("Etc/Universal"));
        Assert.Same(TimeZoneInfo.Utc, ReportDateRangeHelper.ResolveTimeZone(null));
        Assert.Same(TimeZoneInfo.Utc, ReportDateRangeHelper.ResolveTimeZone("  "));
    }

    [Fact]
    public void ResolveTimeZone_IanaId_ReturnsThatZone()
    {
        var tz = ReportDateRangeHelper.ResolveTimeZone("Asia/Kolkata");
        Assert.Equal(TimeSpan.FromHours(5.5), tz.GetUtcOffset(new DateTime(2026, 8, 14, 12, 0, 0, DateTimeKind.Unspecified)));
    }

    [Fact]
    public void ResolveTimeZone_UnknownId_FallsBackToUtc()
    {
        Assert.Same(TimeZoneInfo.Utc, ReportDateRangeHelper.ResolveTimeZone("Not/ARealZone"));
    }

    [Fact]
    public void ResolveUtcRange_NullDates_UsesBoundedLookBackEndingToday()
    {
        var tz = TimeZoneInfo.Utc;
        var todayLocal = DateOnly.FromDateTime(DateTime.UtcNow);
        var (fromUtc, toUtc) = ReportDateRangeHelper.ResolveUtcRange(null, null, "UTC");

        // toUtc is the exclusive start of the local day after today.
        Assert.Equal(todayLocal.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), toUtc);
        // fromUtc is 30 days back (default lookback).
        Assert.Equal(todayLocal.AddDays(-29).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), fromUtc);
        Assert.True(fromUtc < toUtc);
        Assert.True((toUtc - fromUtc).TotalDays <= 31);
    }

    [Fact]
    public void ResolveUtcRange_ExplicitSameDay_ProducesExactlyOneLocalDay()
    {
        var day = new DateOnly(2026, 8, 14);
        var (fromUtc, toUtc) = ReportDateRangeHelper.ResolveUtcRange(day, day, "UTC");

        Assert.Equal(new DateTime(2026, 8, 14, 0, 0, 0, DateTimeKind.Utc), fromUtc);
        Assert.Equal(new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), toUtc);
        Assert.Equal(TimeSpan.FromDays(1), toUtc - fromUtc);
    }

    [Fact]
    public void ResolveUtcRange_NonUtcTimezone_ShiftsBoundariesByOffset()
    {
        // India is UTC+5:30; local midnight maps to the previous day 18:30 UTC.
        var day = new DateOnly(2026, 8, 14);
        var (fromUtc, toUtc) = ReportDateRangeHelper.ResolveUtcRange(day, day, "Asia/Kolkata");

        Assert.Equal(new DateTime(2026, 8, 13, 18, 30, 0, DateTimeKind.Utc), fromUtc);
        Assert.Equal(new DateTime(2026, 8, 14, 18, 30, 0, DateTimeKind.Utc), toUtc);
    }

    [Fact]
    public void ResolveUtcRange_FromAfterTo_ClampsToSingleDay()
    {
        var (fromUtc, toUtc) = ReportDateRangeHelper.ResolveUtcRange(new DateOnly(2026, 8, 20), new DateOnly(2026, 8, 14), "UTC");
        Assert.Equal(fromUtc, toUtc.AddDays(-1));
    }

    [Fact]
    public void ResolveUtcRange_LookBackDaysIsHonoured()
    {
        var day = new DateOnly(2026, 8, 14);
        var (fromUtc, toUtc) = ReportDateRangeHelper.ResolveUtcRange(null, day, "UTC", lookBackDays: 7);

        Assert.Equal(new DateTime(2026, 8, 8, 0, 0, 0, DateTimeKind.Utc), fromUtc);
        Assert.Equal(new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), toUtc);
    }
}
