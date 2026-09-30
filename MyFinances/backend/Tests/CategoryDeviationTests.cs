using MyFinances.Api.Dashboard;
using Xunit;

namespace MyFinances.Api.Tests;

public class CategoryDeviationTests
{
    private static readonly DateOnly Today = new(2026, 3, 10);

    private static (DateOnly Date, decimal Amount) Row(int year, int month, int day, decimal amount) =>
        (new DateOnly(year, month, day), amount);

    [Fact]
    public void Calculate_NoPriorHistory_ReturnsNull()
    {
        var result = CategoryDeviation.Calculate([], 500m, Today);

        Assert.Null(result);
    }

    [Fact]
    public void Calculate_IsPaceAdjusted_OnlyCountsPriorSpendUpToTodaysDayOfMonth()
    {
        // By day 10: Jan 100, Feb 200 -> average 150 over N=2. Later-in-month rows are ignored.
        var prior = new[]
        {
            Row(2026, 1, 5, 100m), Row(2026, 1, 20, 900m),
            Row(2026, 2, 8, 200m), Row(2026, 2, 25, 500m),
        };

        var result = CategoryDeviation.Calculate(prior, 600m, Today);

        Assert.Equal(150m, result!.Value.AverageToDate);
        Assert.Equal("above", result.Value.Deviation);
    }

    [Fact]
    public void Calculate_TransactionsAfterTodaysDayOfMonth_AreExcluded()
    {
        // Day 31 is outside a day-10 window; it still anchors the first month (N=1).
        var prior = new[] { Row(2026, 2, 5, 100m), Row(2026, 2, 28, 999m) };

        var result = CategoryDeviation.Calculate(prior, 100m, Today);

        Assert.Equal(100m, result!.Value.AverageToDate);
        Assert.Equal("inLine", result.Value.Deviation);
    }

    [Theory]
    [InlineData(110, "inLine")]
    [InlineData(90, "inLine")]
    [InlineData(110.01, "above")]
    [InlineData(89.99, "below")]
    [InlineData(100, "inLine")]
    public void Calculate_BandBoundaries(double current, string expected)
    {
        var prior = new[] { Row(2026, 2, 5, 100m) };

        var result = CategoryDeviation.Calculate(prior, (decimal)current, Today);

        Assert.Equal(expected, result!.Value.Deviation);
    }

    [Fact]
    public void Calculate_ZeroSpendMonthsAreCounted()
    {
        // Jan 400, Feb 0, Mar 200 -> 600 / 3 = 200 (today is in April).
        var prior = new[] { Row(2026, 1, 3, 400m), Row(2026, 3, 4, 200m) };

        var result = CategoryDeviation.Calculate(prior, 200m, new DateOnly(2026, 4, 10));

        Assert.Equal(200m, result!.Value.AverageToDate);
        Assert.Equal("inLine", result.Value.Deviation);
    }

    [Fact]
    public void Calculate_PartialFirstMonth_GivesZeroAverageAndAbove()
    {
        // First spend on the 25th of last month: nothing by day 10 -> avg 0, N=1.
        var prior = new[] { Row(2026, 2, 25, 50m) };

        var result = CategoryDeviation.Calculate(prior, 30m, Today);

        Assert.Equal(0m, result!.Value.AverageToDate);
        Assert.Equal("above", result.Value.Deviation);
    }

    [Fact]
    public void Calculate_ZeroAverageAndZeroCurrent_IsInLine()
    {
        var prior = new[] { Row(2026, 2, 25, 50m) };

        var result = CategoryDeviation.Calculate(prior, 0m, Today);

        Assert.Equal("inLine", result!.Value.Deviation);
    }

    [Fact]
    public void Calculate_YearBoundary_IncludesDecemberWhenTodayIsInJanuary()
    {
        var prior = new[] { Row(2026, 12, 5, 100m) };

        var result = CategoryDeviation.Calculate(prior, 300m, new DateOnly(2027, 1, 10));

        Assert.Equal(100m, result!.Value.AverageToDate);
        Assert.Equal("above", result.Value.Deviation);
    }
}
