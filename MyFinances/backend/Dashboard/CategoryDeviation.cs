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
        DateOnly today,
        bool fullMonth = false)
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

        // A fully elapsed month compares like for like on whole months, so no day-of-month cutoff.
        var priorToDate = priorSpend.Where(r => fullMonth || r.Date.Day <= today.Day).Sum(r => r.Amount);
        var average = priorToDate / monthCount;

        return (average, Classify(currentToDate, average));
    }

    // Window mode for rolling or custom periods: compares the period total against the average
    // total of the preceding windows of the same length, walking back from the day before `from`
    // to the category's first spend (windows without spend count as zero). `priorSpend` holds the
    // category's spend rows dated before `from`; returns null when there are none.
    public static (decimal AverageToDate, string Deviation)? CalculateWindow(
        IReadOnlyCollection<(DateOnly Date, decimal Amount)> priorSpend,
        decimal periodTotal,
        DateOnly from,
        int windowDays)
    {
        if (priorSpend.Count == 0 || windowDays <= 0)
        {
            return null;
        }

        var first = priorSpend.Min(r => r.Date);
        var spanDays = from.DayNumber - first.DayNumber;
        if (spanDays <= 0)
        {
            return null;
        }

        var windowCount = (spanDays + windowDays - 1) / windowDays;
        var average = priorSpend.Sum(r => r.Amount) / windowCount;

        return (average, Classify(periodTotal, average));
    }

    private static string Classify(decimal value, decimal average) =>
        value > average * UpperFactor ? Above
        : value < average * LowerFactor ? Below
        : InLine;
}
