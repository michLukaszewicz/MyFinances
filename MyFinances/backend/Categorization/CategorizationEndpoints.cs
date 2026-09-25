using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MyFinances.Api.Transactions;

namespace MyFinances.Api.Categorization;

public static class CategorizationEndpoints
{
    // Nested under the /api group so /api/categorization/* inherits RequireAuthorization(),
    // matching AccountEndpoints' convention.
    public static void MapCategorizationEndpoints(this IEndpointRouteBuilder api)
    {
        var categorization = api.MapGroup("/categorization");

        categorization.MapGet("/categories", async (AppDbContext db) =>
        {
            var categories = await db.Categories
                .OrderBy(c => c.SortOrder)
                .Select(c => new CategoryDto(c.Id, c.Name))
                .ToListAsync();

            return Results.Ok(categories);
        });

        categorization.MapGet("/queue", async (
            AppDbContext db,
            TransferDetectionService transferDetection,
            UserManager<AppUser> userManager,
            ClaimsPrincipal principal) =>
        {
            var userId = principal.GetUserId(userManager);

            // Transfer detection must run before the query executes within the same request,
            // not as a separate background pass (see plan.md's Critical Implementation Details).
            await transferDetection.DetectAsync(userId, db);

            var queue = await db.Transactions
                .Include(t => t.Account)
                .Include(t => t.Category)
                .Where(t => t.UserId == userId && t.CategoryId == null && !t.IsInternalTransfer)
                .OrderBy(t => t.Date).ThenBy(t => t.Id)
                .ToListAsync();

            return Results.Ok(queue.Select(ToDto));
        });

        categorization.MapGet("/handled", async (
            AppDbContext db,
            TransferDetectionService transferDetection,
            UserManager<AppUser> userManager,
            ClaimsPrincipal principal) =>
        {
            var userId = principal.GetUserId(userManager);

            await transferDetection.DetectAsync(userId, db);

            var handled = await db.Transactions
                .Include(t => t.Account)
                .Include(t => t.Category)
                .Where(t => t.UserId == userId && (t.CategoryId != null || t.IsInternalTransfer))
                .OrderByDescending(t => t.Date).ThenBy(t => t.Id)
                .ToListAsync();

            return Results.Ok(handled.Select(ToDto));
        });

        categorization.MapPut("/transactions/{id:guid}", async (
            Guid id,
            CategorizeRequest request,
            AppDbContext db,
            UserManager<AppUser> userManager,
            ClaimsPrincipal principal) =>
        {
            var userId = principal.GetUserId(userManager);

            var transaction = await db.Transactions
                .Include(t => t.Account)
                .Include(t => t.Category)
                .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId);
            if (transaction is null)
            {
                return Results.NotFound();
            }

            if (request.CategoryId is not null)
            {
                var categoryExists = await db.Categories.AnyAsync(c => c.Id == request.CategoryId);
                if (!categoryExists)
                {
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Unknown category.");
                }

                transaction.CategoryId = request.CategoryId;
            }

            if (request.IsInternalTransfer is not null)
            {
                transaction.IsInternalTransfer = request.IsInternalTransfer.Value;
                // An explicit decision permanently opts this row out of future automatic
                // detection passes (see plan.md's Critical Implementation Details).
                transaction.TransferFlagManuallySet = true;
            }

            await db.SaveChangesAsync();

            // Reload the Category navigation in case CategoryId just changed.
            await db.Entry(transaction).Reference(t => t.Category).LoadAsync();

            return Results.Ok(ToDto(transaction));
        })
        .AddEndpointFilter(RequireValidAntiforgery);
    }

    private static TransactionQueueItemDto ToDto(Transaction t) => new(
        t.Id,
        t.Date,
        t.Description,
        t.Amount,
        t.Account.BankName,
        t.Account.AccountNumber,
        t.CategoryId,
        t.Category?.Name,
        t.IsInternalTransfer);

    private static async ValueTask<object?> RequireValidAntiforgery(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var antiforgery = context.HttpContext.RequestServices.GetRequiredService<IAntiforgery>();
        try
        {
            await antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid antiforgery token");
        }

        return await next(context);
    }
}
