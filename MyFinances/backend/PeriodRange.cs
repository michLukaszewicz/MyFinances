namespace MyFinances.Api;

// Inclusive date period shared by the dashboard charts and the transactions list so a chart slice
// and its drilldown list always cover the same days.
public readonly record struct PeriodRange(DateOnly Start, DateOnly End)
{
    // True for exactly one whole calendar month (1st through last day).
    public bool IsFullCalendarMonth => Start.Day == 1 && End == CurrentMonthRange.MonthOf(Start).End;

    public int DayCount => End.DayNumber - Start.DayNumber + 1;

    // Both dates absent -> the month containing `today`. Exactly one present, from > to, a start after
    // today, or an end after today all fail, except the current calendar month whose end is allowed to
    // lie in the future (future-dated rows inside it still count).
    public static bool TryResolve(DateOnly? from, DateOnly? to, DateOnly today, out PeriodRange range, out string error)
    {
        range = default;
        error = "";

        if (from is null && to is null)
        {
            var month = CurrentMonthRange.MonthOf(today);
            range = new PeriodRange(month.Start, month.End);
            return true;
        }

        if (from is null || to is null)
        {
            error = "Both 'from' and 'to' must be provided together.";
            return false;
        }

        if (from > to)
        {
            error = "'from' must not be after 'to'.";
            return false;
        }

        if (from > today)
        {
            error = "'from' must not be in the future.";
            return false;
        }

        var candidate = new PeriodRange(from.Value, to.Value);
        var currentMonth = CurrentMonthRange.MonthOf(today);
        var isCurrentMonth = candidate.Start == currentMonth.Start && candidate.End == currentMonth.End;
        if (to > today && !isCurrentMonth)
        {
            error = "'to' must not be in the future.";
            return false;
        }

        range = candidate;
        return true;
    }
}
