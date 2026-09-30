using Xunit;
using MyFinances.Api;

namespace MyFinances.Api.Tests;

public class CurrentMonthRangeTests
{
    [Fact]
    public void Get_MidMonth_ReturnsFirstAndLastDayOfThatMonth()
    {
        var (start, end) = CurrentMonthRange.Get(new FixedTimeProvider(new DateTimeOffset(2026, 2, 14, 12, 0, 0, TimeSpan.Zero)));

        Assert.Equal(new DateOnly(2026, 2, 1), start);
        Assert.Equal(new DateOnly(2026, 2, 28), end);
    }

    [Fact]
    public void Get_LateOnLastUtcDayOfMonth_RollsOverInWarsawTime()
    {
        // 23:30 UTC on 30 Sep is 01:30 on 1 Oct in Warsaw (UTC+2). Skipped when tzdata is absent.
        if (TimeZoneInfo.TryFindSystemTimeZoneById("Europe/Warsaw", out _) is false)
        {
            return;
        }

        var (start, _) = CurrentMonthRange.Get(new FixedTimeProvider(new DateTimeOffset(2026, 9, 30, 23, 30, 0, TimeSpan.Zero)));

        Assert.Equal(new DateOnly(2026, 10, 1), start);
    }
}
