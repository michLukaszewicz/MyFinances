using Xunit;
using MyFinances.Api;

namespace MyFinances.Api.Tests;

public class CurrentMonthRangeTests
{
    [Fact]
    public void Get_MidMonth_ReturnsFirstAndLastDayOfThatMonth()
    {
        // Act
        var (start, end) = CurrentMonthRange.Get(new FixedTimeProvider(new DateTimeOffset(2026, 2, 14, 12, 0, 0, TimeSpan.Zero)));

        // Assert
        Assert.Equal(new DateOnly(2026, 2, 1), start);
        Assert.Equal(new DateOnly(2026, 2, 28), end);
    }

    [Fact]
    public void Get_LateOnLastUtcDayOfMonth_RollsOverInWarsawTime()
    {
        // Arrange
        // 23:30 UTC on 30 Sep is 01:30 on 1 Oct in Warsaw (UTC+2). Skipped when tzdata is absent.
        if (TimeZoneInfo.TryFindSystemTimeZoneById("Europe/Warsaw", out _) is false)
        {
            return;
        }

        // Act
        var (start, _) = CurrentMonthRange.Get(new FixedTimeProvider(new DateTimeOffset(2026, 9, 30, 23, 30, 0, TimeSpan.Zero)));

        // Assert
        Assert.Equal(new DateOnly(2026, 10, 1), start);
    }

    [Fact]
    public void Today_LateOnUtcDay_IsNextDayInWarsawTime()
    {
        // Arrange
        // 23:30 UTC on 9 Sep is 01:30 on 10 Sep in Warsaw (UTC+2). Skipped when tzdata is absent.
        if (TimeZoneInfo.TryFindSystemTimeZoneById("Europe/Warsaw", out _) is false)
        {
            return;
        }

        // Act
        var today = CurrentMonthRange.Today(new FixedTimeProvider(new DateTimeOffset(2026, 9, 9, 23, 30, 0, TimeSpan.Zero)));

        // Assert
        Assert.Equal(new DateOnly(2026, 9, 10), today);
    }

    [Fact]
    public void MonthOf_ReturnsFirstAndLastDayOfTheGivenDaysMonth()
    {
        // Act
        var (start, end) = CurrentMonthRange.MonthOf(new DateOnly(2028, 2, 10));

        // Assert
        Assert.Equal(new DateOnly(2028, 2, 1), start);
        Assert.Equal(new DateOnly(2028, 2, 29), end);
    }
}
