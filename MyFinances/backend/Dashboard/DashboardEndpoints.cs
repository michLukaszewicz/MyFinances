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
