using MyFinances.Api.Dashboard;
using Xunit;

namespace MyFinances.Api.Tests.Dashboard;

public class CategoryDeviationTests
{
    private static readonly DateOnly Today = new(2026, 3, 10);

    private static (DateOnly Date, decimal Amount) Row(int year, int month, int day, decimal amount) =>
        (new DateOnly(year, month, day), amount);

    [Fact]
    public void Calculate_NoPriorHistory_ReturnsNull()
    {
        // Act
        var result = CategoryDeviation.Calculate([], 500m, Today);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void Calculate_IsPaceAdjusted_OnlyCountsPriorSpendUpToTodaysDayOfMonth()
    {
        // Arrange
        // By day 10: Jan 100, Feb 200 -> average 150 over N=2. Later-in-month rows are ignored.
        var prior = new[]
        {
            Row(2026, 1, 5, 100m), Row(2026, 1, 20, 900m),
            Row(2026, 2, 8, 200m), Row(2026, 2, 25, 500m),
        };

        // Act
        var result = CategoryDeviation.Calculate(prior, 600m, Today);

        // Assert
        Assert.Equal(150m, result!.Value.AverageToDate);
        Assert.Equal("above", result.Value.Deviation);
    }

    [Fact]
    public void Calculate_TransactionsAfterTodaysDayOfMonth_AreExcluded()
    {
        // Arrange
        // Day 31 is outside a day-10 window; it still anchors the first month (N=1).
        var prior = new[] { Row(2026, 2, 5, 100m), Row(2026, 2, 28, 999m) };

        // Act
        var result = CategoryDeviation.Calculate(prior, 100m, Today);

        // Assert
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
        // Arrange
        var prior = new[] { Row(2026, 2, 5, 100m) };

        // Act
        var result = CategoryDeviation.Calculate(prior, (decimal)current, Today);

        // Assert
        Assert.Equal(expected, result!.Value.Deviation);
    }

    [Fact]
    public void Calculate_ZeroSpendMonthsAreCounted()
    {
        // Arrange
        // Jan 400, Feb 0, Mar 200 -> 600 / 3 = 200 (today is in April).
        var prior = new[] { Row(2026, 1, 3, 400m), Row(2026, 3, 4, 200m) };

        // Act
        var result = CategoryDeviation.Calculate(prior, 200m, new DateOnly(2026, 4, 10));

        // Assert
        Assert.Equal(200m, result!.Value.AverageToDate);
        Assert.Equal("inLine", result.Value.Deviation);
    }

    [Fact]
    public void Calculate_PartialFirstMonth_GivesZeroAverageAndAbove()
    {
        // Arrange
        // First spend on the 25th of last month: nothing by day 10 -> avg 0, N=1.
        var prior = new[] { Row(2026, 2, 25, 50m) };

        // Act
        var result = CategoryDeviation.Calculate(prior, 30m, Today);

        // Assert
        Assert.Equal(0m, result!.Value.AverageToDate);
        Assert.Equal("above", result.Value.Deviation);
    }

    [Fact]
    public void Calculate_ZeroAverageAndZeroCurrent_IsInLine()
    {
        // Arrange
        var prior = new[] { Row(2026, 2, 25, 50m) };

        // Act
        var result = CategoryDeviation.Calculate(prior, 0m, Today);

        // Assert
        Assert.Equal("inLine", result!.Value.Deviation);
    }

    [Fact]
    public void Calculate_YearBoundary_IncludesDecemberWhenTodayIsInJanuary()
    {
        // Arrange
        var prior = new[] { Row(2026, 12, 5, 100m) };

        // Act
        var result = CategoryDeviation.Calculate(prior, 300m, new DateOnly(2027, 1, 10));

        // Assert
        Assert.Equal(100m, result!.Value.AverageToDate);
        Assert.Equal("above", result.Value.Deviation);
    }

    [Fact]
    public void Calculate_PastMonthAsOfMonthEnd_ComparesFullMonthAgainstEarlierMonths()
    {
        // Arrange
        // Displayed month is Feb (as-of Feb 28): Dec 100, Jan 0 (zero month counts) -> N=2, average 50.
        var prior = new[] { Row(2025, 12, 31, 100m) };

        // Act
        var result = CategoryDeviation.Calculate(prior, 80m, new DateOnly(2026, 2, 28), fullMonth: true);

        // Assert
        Assert.Equal(50m, result!.Value.AverageToDate);
        Assert.Equal("above", result.Value.Deviation);
    }

    [Fact]
    public void Calculate_PastMonthWithNoEarlierMonth_ReturnsNull()
    {
        // Arrange
        var prior = new[] { Row(2026, 2, 3, 100m) };

        // Act & Assert
        Assert.Null(CategoryDeviation.Calculate(prior, 80m, new DateOnly(2026, 2, 28), fullMonth: true));
    }

    [Fact]
    public void CalculateWindow_NoPriorSpend_ReturnsNull()
    {
        // Act & Assert
        Assert.Null(CategoryDeviation.CalculateWindow([], 100m, new DateOnly(2026, 3, 1), 30));
    }

    [Fact]
    public void CalculateWindow_ExactMultipleOfWindows_AveragesPerWindow()
    {
        // Arrange
        // First spend 60 days before `from` -> exactly 2 windows of 30 days; 200 / 2 = 100.
        var from = new DateOnly(2026, 3, 1);
        var prior = new[] { Row(2026, 1, 1, 120m), Row(2026, 2, 10, 80m) };

        // Act
        var result = CategoryDeviation.CalculateWindow(prior, 150m, from, 30);

        // Assert
        Assert.Equal(100m, result!.Value.AverageToDate);
        Assert.Equal("above", result.Value.Deviation);
    }

    [Fact]
    public void CalculateWindow_PartialFirstWindow_CountsItAsAWholeWindow()
    {
        // Arrange
        // 39 days back -> ceil(39/30) = 2 windows.
        var from = new DateOnly(2026, 2, 9);
        var prior = new[] { Row(2026, 1, 1, 200m) };

        // Act
        var result = CategoryDeviation.CalculateWindow(prior, 100m, from, 30);

        // Assert
        Assert.Equal(100m, result!.Value.AverageToDate);
        Assert.Equal("inLine", result.Value.Deviation);
    }

    [Theory]
    [InlineData(110, "inLine")]
    [InlineData(111, "above")]
    [InlineData(90, "inLine")]
    [InlineData(89, "below")]
    public void CalculateWindow_BandEdgesAreInclusive(int total, string expected)
    {
        // Arrange
        var from = new DateOnly(2026, 2, 1);
        var prior = new[] { Row(2026, 1, 2, 100m) };

        // Act
        var result = CategoryDeviation.CalculateWindow(prior, total, from, 30);

        // Assert
        Assert.Equal(expected, result!.Value.Deviation);
    }

    [Fact]
    public void CalculateWindow_AcrossYearBoundary_UsesDayDifference()
    {
        // Arrange
        // 2025-12-02 .. 2026-01-01 is exactly 30 days -> one window.
        var prior = new[] { Row(2025, 12, 2, 60m) };

        // Act
        var result = CategoryDeviation.CalculateWindow(prior, 60m, new DateOnly(2026, 1, 1), 30);

        // Assert
        Assert.Equal(60m, result!.Value.AverageToDate);
        Assert.Equal("inLine", result.Value.Deviation);
    }
}
