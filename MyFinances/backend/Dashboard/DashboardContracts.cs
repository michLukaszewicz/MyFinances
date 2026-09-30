namespace MyFinances.Api.Dashboard;

// Amount is the positive total spent in the category for the current month.
public record CategorySpendDto(Guid CategoryId, string CategoryName, decimal Amount);

// Spend-endpoint variant: Amount is still the full current-month total; AverageToDate is the
// category's average spend over the same day-of-month window in prior months, and Deviation
// ("above" | "below" | "inLine") compares spend-to-date against it. Both are null when the
// category has no prior-month history.
public record CategorySpendSignalDto(
    Guid CategoryId,
    string CategoryName,
    decimal Amount,
    decimal? AverageToDate,
    string? Deviation);
