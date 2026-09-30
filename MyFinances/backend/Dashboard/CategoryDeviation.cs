namespace MyFinances.Api.Dashboard;

// Pace-adjusted comparison of a category's current-month spend-to-date against its average
// spend over the same day-of-month window in prior months. Pure: no DB access.
public static class CategoryDeviation
{
    public const string Above = "above";
    public const string Below = "below";
    public const string InLine = "inLine";

    private const decimal UpperFactor = 1.1m;
    private const decimal LowerFactor = 0.9m;

    // priorSpend: the category's spend rows dated before the current month (positive magnitudes).
    // currentToDate: current-month spend dated from the 1st through today.
    // Returns null when there is no prior-month history (first spend is in the current month).
    public static (decimal AverageToDate, string Deviation)? Calculate(
        IReadOnlyCollection<(DateOnly Date, decimal Amount)> priorSpend,
        decimal currentToDate,
        DateOnly today)
    {
        if (priorSpend.Count == 0)
        {
            return null;
        }

        // Window runs from the month of the first-ever spend up to last month; zero months count.
        var first = priorSpend.Min(r => r.Date);
        var monthCount = (today.Year * 12 + today.Month) - (first.Year * 12 + first.Month);
        if (monthCount <= 0)
        {
            return null;
        }

        var priorToDate = priorSpend.Where(r => r.Date.Day <= today.Day).Sum(r => r.Amount);
        var average = priorToDate / monthCount;

        var deviation = currentToDate > average * UpperFactor ? Above
            : currentToDate < average * LowerFactor ? Below
            : InLine;

        return (average, deviation);
    }
}
