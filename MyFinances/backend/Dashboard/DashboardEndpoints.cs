using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MyFinances.Api.Categorization;
using MyFinances.Api.Transactions;

namespace MyFinances.Api.Dashboard;

public static class DashboardEndpoints
{
    // Nested under the /api group so /api/dashboard/* inherits RequireAuthorization().
    // GET-only, so no antiforgery filter.
    public static void MapDashboardEndpoints(this IEndpointRouteBuilder api)
    {
        var dashboard = api.MapGroup("/dashboard");

        // Spend: negative amounts, reported as positive magnitudes.
        dashboard.MapGet("/category-spend", (
            AppDbContext db,
            TransferDetectionService transferDetection,
            UserManager<AppUser> userManager,
            ClaimsPrincipal principal,
            TimeProvider clock) =>
            GetCategoryTotalsAsync(db, transferDetection, userManager, principal, clock, income: false));

        // Income: positive amounts (salary, refunds, ...), categorized and non-transfer.
        dashboard.MapGet("/category-income", (
            AppDbContext db,
            TransferDetectionService transferDetection,
            UserManager<AppUser> userManager,
            ClaimsPrincipal principal,
            TimeProvider clock) =>
            GetCategoryTotalsAsync(db, transferDetection, userManager, principal, clock, income: true));
    }

    private static async Task<IResult> GetCategoryTotalsAsync(
        AppDbContext db,
        TransferDetectionService transferDetection,
        UserManager<AppUser> userManager,
        ClaimsPrincipal principal,
        TimeProvider clock,
        bool income)
    {
        var userId = principal.GetUserId(userManager);

        // Transfer detection must run before the query executes (same ordering as
        // CategorizationEndpoints), so IsInternalTransfer is current when we exclude it.
        await transferDetection.DetectAsync(userId, db);

        // Read the clock once so `today` and the month range can never straddle a boundary.
        var today = CurrentMonthRange.Today(clock);
        var range = CurrentMonthRange.MonthOf(today);

        var rows = await db.Transactions
            .AsNoTracking()
            .Include(t => t.Category)
            .Where(t => t.UserId == userId
                && t.CategoryId != null
                && !t.IsInternalTransfer
                && (income ? t.Amount > 0 : t.Amount < 0)
                && t.Date >= range.Start
                && t.Date <= range.End)
            .ToListAsync();

        // Grouped in memory: DB-side GroupBy over this shape doesn't reliably translate
        // (same precedent as ImportEndpoints).
        var groups = rows
            .GroupBy(t => t.CategoryId)
            .OrderBy(g => g.First().Category!.SortOrder)
            .ToList();

        if (income)
        {
            return Results.Ok(groups
                .Select(g => new CategorySpendDto(
                    g.First().Category!.Id,
                    g.First().Category!.Name,
                    g.Sum(t => t.Amount)))
                .ToList());
        }

        // Spend signal: compare spend-to-date against the same day-of-month window in prior
        // months. History only decorates categories that already have current-month spend.
        var categoryIds = groups.Select(g => g.First().Category!.Id).ToList();
        var history = categoryIds.Count == 0
            ? new Dictionary<Guid, List<(DateOnly Date, decimal Amount)>>()
            : (await db.Transactions
                .AsNoTracking()
                .Where(t => t.UserId == userId
                    && t.CategoryId != null
                    && categoryIds.Contains(t.CategoryId.Value)
                    && !t.IsInternalTransfer
                    && t.Amount < 0
                    && t.Date < range.Start)
                .Select(t => new { CategoryId = t.CategoryId!.Value, t.Date, t.Amount })
                .ToListAsync())
                .GroupBy(t => t.CategoryId)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(t => (t.Date, Amount: -t.Amount)).ToList());

        var spendResult = groups
            .Select(g =>
            {
                var categoryId = g.First().Category!.Id;
                var toDate = g.Where(t => t.Date <= today).Sum(t => -t.Amount);
                var signal = history.TryGetValue(categoryId, out var prior)
                    ? CategoryDeviation.Calculate(prior, toDate, today)
                    : null;
                return new CategorySpendSignalDto(
                    categoryId,
                    g.First().Category!.Name,
                    g.Sum(t => -t.Amount),
                    signal?.AverageToDate,
                    signal?.Deviation);
            })
            .ToList();

        return Results.Ok(spendResult);
    }
}
