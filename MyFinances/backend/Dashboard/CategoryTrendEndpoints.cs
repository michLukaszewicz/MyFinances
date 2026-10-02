using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MyFinances.Api.Categorization;
using MyFinances.Api.Transactions;

namespace MyFinances.Api.Dashboard;

// Bucket boundaries are clipped to the requested range; amounts are aligned with the buckets array.
public record TrendBucketDto(DateOnly Start, DateOnly End);

public record TrendSeriesDto(Guid CategoryId, string CategoryName, IReadOnlyList<decimal> Amounts, decimal Total);

public record CategoryTrendDto(IReadOnlyList<TrendBucketDto> Buckets, IReadOnlyList<TrendSeriesDto> Series);

public static class CategoryTrendEndpoints
{
    private const int MaxBuckets = 120;

    // Nested under the /api group so it inherits RequireAuthorization(). GET-only, no antiforgery.
    public static void MapCategoryTrendEndpoints(this IEndpointRouteBuilder api)
    {
        var dashboard = api.MapGroup("/dashboard");

        dashboard.MapGet("/category-trend", async (
            string? granularity,
            string? kind,
            DateOnly from,
            DateOnly to,
            AppDbContext db,
            TransferDetectionService transferDetection,
            UserManager<AppUser> userManager,
            ClaimsPrincipal principal,
            TimeProvider clock) =>
        {
            var income = string.Equals(kind, "income", StringComparison.OrdinalIgnoreCase);
            if (!income && !string.Equals(kind, "spend", StringComparison.OrdinalIgnoreCase))
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "'kind' must be 'spend' or 'income'.");
            }

            var normalized = granularity?.ToLowerInvariant();
            if (normalized is not ("week" or "month" or "year"))
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "'granularity' must be 'week', 'month' or 'year'.");
            }

            if (from > to)
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "'from' must not be after 'to'.");
            }

            // Read the clock once; the current, unfinished bucket is allowed but the future is not.
            if (to > CurrentMonthRange.Today(clock))
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "'to' must not be in the future.");
            }

            var buckets = BuildBuckets(normalized, from, to);
            if (buckets is null)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: $"The range spans more than {MaxBuckets} buckets.");
            }

            var userId = principal.GetUserId(userManager);

            // Same ordering as the donuts: transfer detection first so IsInternalTransfer is current.
            await transferDetection.DetectAsync(userId, db);

            var rows = await db.Transactions
                .AsNoTracking()
                .Include(t => t.Category)
                .Where(t => t.UserId == userId
                    && t.CategoryId != null
                    && !t.IsInternalTransfer
                    && (income ? t.Amount > 0 : t.Amount < 0)
                    && t.Date >= from
                    && t.Date <= to)
                .ToListAsync();

            var indexByStart = buckets
                .Select((b, i) => (b.Key, Index: i))
                .ToDictionary(x => x.Key, x => x.Index);

            var series = rows
                .GroupBy(t => t.CategoryId)
                .OrderBy(g => g.First().Category!.SortOrder)
                .Select(g =>
                {
                    var amounts = new decimal[buckets.Count];
                    foreach (var t in g)
                    {
                        amounts[indexByStart[BucketKey(normalized, t.Date)]] += income ? t.Amount : -t.Amount;
                    }

                    var category = g.First().Category!;
                    return new TrendSeriesDto(category.Id, category.Name, amounts, amounts.Sum());
                })
                .Where(s => s.Total != 0)
                .ToList();

            return Results.Ok(new CategoryTrendDto(
                buckets.Select(b => new TrendBucketDto(b.Start, b.End)).ToList(),
                series));
        });
    }

    // First day of the (unclipped) bucket containing `date`: Monday for weeks, 1st of month/year.
    private static DateOnly BucketKey(string granularity, DateOnly date) => granularity switch
    {
        "week" => date.AddDays(-(((int)date.DayOfWeek + 6) % 7)),
        "month" => new DateOnly(date.Year, date.Month, 1),
        _ => new DateOnly(date.Year, 1, 1),
    };

    private static DateOnly NextKey(string granularity, DateOnly key) => granularity switch
    {
        "week" => key.AddDays(7),
        "month" => key.AddMonths(1),
        _ => key.AddYears(1),
    };

    // Null when the range needs more than MaxBuckets buckets.
    private static List<(DateOnly Key, DateOnly Start, DateOnly End)>? BuildBuckets(string granularity, DateOnly from, DateOnly to)
    {
        var buckets = new List<(DateOnly, DateOnly, DateOnly)>();
        for (var key = BucketKey(granularity, from); key <= to; key = NextKey(granularity, key))
        {
            if (buckets.Count == MaxBuckets)
            {
                return null;
            }

            var start = key < from ? from : key;
            var naturalEnd = NextKey(granularity, key).AddDays(-1);
            var end = naturalEnd > to ? to : naturalEnd;
            buckets.Add((key, start, end));
        }

        return buckets;
    }
}
