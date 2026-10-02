using MyFinances.Api;
using Xunit;

namespace MyFinances.Api.Tests.Dashboard;

public class PeriodRangeTests
{
    private static readonly DateOnly Today = new(2099, 3, 10);

    [Fact]
    public void TryResolve_BothAbsent_ReturnsCurrentMonth()
    {
        // Act & Assert
        Assert.True(PeriodRange.TryResolve(null, null, Today, out var range, out _));

        Assert.Equal(new DateOnly(2099, 3, 1), range.Start);
        Assert.Equal(new DateOnly(2099, 3, 31), range.End);
        Assert.True(range.IsFullCalendarMonth);
    }

    [Fact]
    public void TryResolve_OnlyOneBoundGiven_Fails()
    {
        // Act & Assert
        Assert.False(PeriodRange.TryResolve(new DateOnly(2099, 3, 1), null, Today, out _, out var error));
        Assert.NotEmpty(error);
        Assert.False(PeriodRange.TryResolve(null, new DateOnly(2099, 3, 1), Today, out _, out _));
    }

    [Fact]
    public void TryResolve_FromAfterTo_Fails()
    {
        // Act & Assert
        Assert.False(PeriodRange.TryResolve(new DateOnly(2099, 3, 5), new DateOnly(2099, 3, 4), Today, out _, out _));
    }

    [Fact]
    public void TryResolve_FromInFuture_Fails()
    {
        // Act & Assert
        Assert.False(PeriodRange.TryResolve(new DateOnly(2099, 3, 11), new DateOnly(2099, 3, 20), Today, out _, out _));
    }

    [Fact]
    public void TryResolve_ToInFutureOutsideCurrentMonth_Fails()
    {
        // Act & Assert
        Assert.False(PeriodRange.TryResolve(new DateOnly(2099, 2, 1), new DateOnly(2099, 3, 11), Today, out _, out _));
    }

    [Fact]
    public void TryResolve_ExactCurrentMonthWithFutureEnd_Succeeds()
    {
        // Act & Assert
        Assert.True(PeriodRange.TryResolve(new DateOnly(2099, 3, 1), new DateOnly(2099, 3, 31), Today, out var range, out _));
        Assert.True(range.IsFullCalendarMonth);
    }

    [Fact]
    public void TryResolve_CurrentMonthToDate_IsNotAFullCalendarMonth()
    {
        // Act & Assert
        Assert.True(PeriodRange.TryResolve(new DateOnly(2099, 3, 1), new DateOnly(2099, 3, 10), Today, out var range, out _));
        Assert.False(range.IsFullCalendarMonth);
    }

    [Fact]
    public void TryResolve_PastFullMonthAcrossYearBoundary_IsFullCalendarMonth()
    {
        // Arrange
        var today = new DateOnly(2100, 1, 10);

        // Act & Assert
        Assert.True(PeriodRange.TryResolve(new DateOnly(2099, 12, 1), new DateOnly(2099, 12, 31), today, out var range, out _));
        Assert.True(range.IsFullCalendarMonth);
        Assert.Equal(31, range.DayCount);
    }

    [Fact]
    public void TryResolve_RangeSpanningYearBoundary_Succeeds()
    {
        // Arrange
        var today = new DateOnly(2100, 1, 10);

        // Act & Assert
        Assert.True(PeriodRange.TryResolve(new DateOnly(2099, 12, 20), new DateOnly(2100, 1, 5), today, out var range, out _));
        Assert.False(range.IsFullCalendarMonth);
        Assert.Equal(17, range.DayCount);
    }
}
