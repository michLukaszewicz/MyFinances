namespace MyFinances.Api;

// Single source of truth for "current month" (server UTC), shared by the dashboard
// aggregation endpoint and the transactions list filter so both always agree on the boundary.
public static class CurrentMonthRange
{
    public static (DateOnly Start, DateOnly End) Get()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var start = new DateOnly(today.Year, today.Month, 1);
        var end = start.AddMonths(1).AddDays(-1);
        return (start, end);
    }
}
