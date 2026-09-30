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

        // TEMPORARY diagnostic (Development only, aggregates only): shows how the current user's
        // transactions split by month / sign / category / transfer flag, to explain empty charts.
        dashboard.MapGet("/debug", async (
            AppDbContext db,
            IWebHostEnvironment env,
            UserManager<AppUser> userManager,
            ClaimsPrincipal principal) =>
        {
            if (!env.IsDevelopment()) return Results.NotFound();
            var userId = principal.GetUserId(userManager);
            var rows = await db.Transactions.AsNoTracking().Include(t => t.Category)
                .Where(t => t.UserId == userId).ToListAsync();
            var range = CurrentMonthRange.Get();
            var summary = rows
                .GroupBy(t => new
                {
                    Month = $"{t.Date.Year}-{t.Date.Month:00}",
                    Sign = t.Amount < 0 ? "negative" : "positive",
                    Category = t.Category?.Name ?? "(uncategorized)",
                    t.IsInternalTransfer,
                })
                .OrderByDescending(g => g.Key.Month)
                .Select(g => new { g.Key.Month, g.Key.Sign, g.Key.Category, g.Key.IsInternalTransfer, Count = g.Count(), Sum = g.Sum(t => t.Amount) });
            return Results.Ok(new { currentMonth = new { range.Start, range.End }, summary });
        });

        // Spend: negative amounts, reported as positive magnitudes.
        dashboard.MapGet("/category-spend", (
            AppDbContext db,
            TransferDetectionService transferDetection,
            UserManager<AppUser> userManager,
            ClaimsPrincipal principal) =>
            GetCategoryTotalsAsync(db, transferDetection, userManager, principal, income: false));

        // Income: positive amounts (salary, refunds, ...), categorized and non-transfer.
        dashboard.MapGet("/category-income", (
            AppDbContext db,
            TransferDetectionService transferDetection,
            UserManager<AppUser> userManager,
            ClaimsPrincipal principal) =>
            GetCategoryTotalsAsync(db, transferDetection, userManager, principal, income: true));
    }

    private static async Task<IResult> GetCategoryTotalsAsync(
        AppDbContext db,
        TransferDetectionService transferDetection,
        UserManager<AppUser> userManager,
        ClaimsPrincipal principal,
        bool income)
    {
        var userId = principal.GetUserId(userManager);

        // Transfer detection must run before the query executes (same ordering as
        // CategorizationEndpoints), so IsInternalTransfer is current when we exclude it.
        await transferDetection.DetectAsync(userId, db);

        var range = CurrentMonthRange.Get();

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
        var result = rows
            .GroupBy(t => t.CategoryId)
            .OrderBy(g => g.First().Category!.SortOrder)
            .Select(g => new CategorySpendDto(
                g.First().Category!.Id,
                g.First().Category!.Name,
                g.Sum(t => income ? t.Amount : -t.Amount)))
            .ToList();

        return Results.Ok(result);
    }
}
