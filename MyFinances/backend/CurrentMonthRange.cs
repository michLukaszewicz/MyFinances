namespace MyFinances.Api;

// Single source of truth for "current month", shared by the dashboard aggregation endpoint
// and the transactions list filter so both always agree on the boundary. The month rolls over
// in Poland's time zone (the user's), falling back to UTC where tzdata isn't installed
// (e.g. slim container images).
public static class CurrentMonthRange
{
    private const string UserTimeZoneId = "Europe/Warsaw";

    public static (DateOnly Start, DateOnly End) Get(TimeProvider? clock = null)
    {
        var today = Today(clock);
        var start = new DateOnly(today.Year, today.Month, 1);
        var end = start.AddMonths(1).AddDays(-1);
        return (start, end);
    }

    // Today's date in the user's time zone, so day-of-month comparisons agree with Get()'s month boundary.
    public static DateOnly Today(TimeProvider? clock = null)
    {
        var now = (clock ?? TimeProvider.System).GetUtcNow();
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, ResolveTimeZone()).DateTime);
    }

    private static TimeZoneInfo ResolveTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(UserTimeZoneId);
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
}
