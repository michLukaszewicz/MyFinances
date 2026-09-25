using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MyFinances.Api.Transactions;

public static class TransactionEndpoints
{
    private const int DefaultTake = 20;
    private const int MaxTake = 100;

    // Nested under the /api group so /api/transactions inherits RequireAuthorization() — no
    // explicit attribute needed, and no antiforgery filter since this is a GET (only
    // /import/commit and /auth/logout add that filter, per ImportEndpoints.cs's convention).
    public static void MapTransactionEndpoints(this IEndpointRouteBuilder api)
    {
        var transactions = api.MapGroup("/transactions");

        transactions.MapGet("/", async (
            int? skip,
            int? take,
            AppDbContext db,
            UserManager<AppUser> userManager,
            ClaimsPrincipal principal) =>
        {
            var userId = principal.GetUserId(userManager);

            var effectiveSkip = Math.Max(skip ?? 0, 0);
            var effectiveTake = Math.Clamp(take ?? DefaultTake, 1, MaxTake);

            var query = db.Transactions.Where(t => t.UserId == userId);

            // No CreatedAt field exists, and Date alone isn't unique per user, so Id is the
            // deterministic tiebreak for a stable newest-first ordering across pages.
            var items = await query
                .OrderByDescending(t => t.Date)
                .ThenByDescending(t => t.Id)
                .Skip(effectiveSkip)
                .Take(effectiveTake)
                .Select(t => new TransactionListItemDto(t.Id, t.Date, t.Description, t.Amount, t.CategoryId))
                .ToListAsync();

            var totalCount = await query.CountAsync();
            var hasMore = effectiveSkip + items.Count < totalCount;

            return Results.Ok(new TransactionListResponseDto(items, hasMore));
        });
    }
}
