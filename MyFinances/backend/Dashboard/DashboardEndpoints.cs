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

        dashboard.MapGet("/category-spend", async (
            AppDbContext db,
            TransferDetectionService transferDetection,
            UserManager<AppUser> userManager,
            ClaimsPrincipal principal) =>
        {
            var userId = principal.GetUserId(userManager);

            // Transfer detection must run before the query executes (same ordering as
            // CategorizationEndpoints), so IsInternalTransfer is current when we exclude it.
            await transferDetection.DetectAsync(userId, db);

            var range = CurrentMonthRange.Get();

            var rows = await db.Transactions
                .Include(t => t.Category)
                .Where(t => t.UserId == userId
                    && t.CategoryId != null
                    && !t.IsInternalTransfer
                    && t.Amount < 0
                    && t.Date >= range.Start
                    && t.Date <= range.End)
                .ToListAsync();

            // Grouped in memory: DB-side GroupBy over this shape doesn't reliably translate
            // (same precedent as ImportEndpoints).
            var result = rows
                .GroupBy(t => t.Category!)
                .OrderBy(g => g.Key.SortOrder)
                .Select(g => new CategorySpendDto(g.Key.Id, g.Key.Name, g.Sum(t => -t.Amount)))
                .ToList();

            return Results.Ok(result);
        });
    }
}
