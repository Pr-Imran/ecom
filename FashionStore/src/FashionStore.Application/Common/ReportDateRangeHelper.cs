namespace FashionStore.Application.Common;

/// <summary>
/// Resolves admin-entered local date ranges into inclusive UTC boundaries for
/// reporting queries. The store timezone is supplied by the caller (from the
/// commerce settings) so reports bucketed by "today" and "this month" follow the
/// store's local day, not UTC. When no dates are supplied a bounded look-back
/// window is used so report queries always carry a date limit.
/// </summary>
public static class ReportDateRangeHelper
{
    private const int DefaultLookBackDays = 30;

    /// <summary>Parses an IANA timezone id, treating UTC aliases as <see cref="TimeZoneInfo.Utc"/>.</summary>
    public static TimeZoneInfo ResolveTimeZone(string? timezoneId)
    {
        if (string.IsNullOrWhiteSpace(timezoneId))
        {
            return TimeZoneInfo.Utc;
        }

        var id = timezoneId.Trim();
        if (id is "UTC" or "Etc/UTC" or "GMT" or "Etc/GMT" or "Etc/Universal" or "Etc/Zulu" or "Universal" or "Zulu")
        {
            return TimeZoneInfo.Utc;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    /// <summary>
    /// Returns an inclusive UTC range covering the local dates given. The upper
    /// bound is the exclusive start of the day after <paramref name="toDate"/>.
    /// </summary>
    public static (DateTime FromUtc, DateTime ToUtc) ResolveUtcRange(
        DateOnly? fromDate,
        DateOnly? toDate,
        string? timezoneId,
        int lookBackDays = DefaultLookBackDays)
    {
        lookBackDays = Math.Max(1, lookBackDays);
        var tz = ResolveTimeZone(timezoneId);
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        var todayLocal = DateOnly.FromDateTime(nowLocal);

        var fromLocal = fromDate ?? todayLocal.AddDays(-(lookBackDays - 1));
        var toLocal = toDate ?? todayLocal;

        if (fromLocal > toLocal)
        {
            fromLocal = toLocal;
        }

        var fromUtc = TimeZoneInfo.ConvertTimeToUtc(fromLocal.ToDateTime(TimeOnly.MinValue), tz);
        var toUtc = TimeZoneInfo.ConvertTimeToUtc(toLocal.AddDays(1).ToDateTime(TimeOnly.MinValue), tz);

        return (fromUtc, toUtc);
    }
}
