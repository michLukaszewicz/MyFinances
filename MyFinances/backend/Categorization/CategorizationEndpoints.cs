using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MyFinances.Api.Transactions;
using System.Security.Claims;

namespace MyFinances.Api.Categorization;

public static class CategorizationEndpoints
{
    private const int MaxCategoryNameLength = 50;

    // New categories sort after the starter set, alphabetically among themselves.
    private const int NewCategorySortOrder = 1000;

    // Nested under the /api group so /api/categorization/* inherits RequireAuthorization(),
    // matching AccountEndpoints' convention.
    public static void MapCategorizationEndpoints(this IEndpointRouteBuilder api)
    {
        var categorization = api.MapGroup("/categorization");
        categorization.MapGet("/categories", async (
            AppDbContext db,
            UserManager<AppUser> userManager,
            ClaimsPrincipal principal) =>
        {
            var userId = principal.GetUserId(userManager);
            var categories = await db.Categories.OwnedBy(userId)
                .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
                .ToListAsync();
            return Results.Ok(categories.Select(ToDto));
        });
        categorization.MapPost("/categories", async (
            CategoryWriteRequest request,
            AppDbContext db,
            UserManager<AppUser> userManager,
            ClaimsPrincipal principal) =>
        {
            var userId = principal.GetUserId(userManager);
            var name = request.Name?.Trim() ?? "";
            if (name.Length == 0 || name.Length > MaxCategoryNameLength)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: $"Category name must be between 1 and {MaxCategoryNameLength} characters.");
            }
            CategoryKind kind;
            if (string.Equals(request.Kind, "income", StringComparison.OrdinalIgnoreCase))
            {
                kind = CategoryKind.Income;
            }
            else if (string.Equals(request.Kind, "expense", StringComparison.OrdinalIgnoreCase))
            {
                kind = CategoryKind.Expense;
            }
            else
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Category kind must be expense or income.");
            }
            var existing = await db.Categories.OwnedBy(userId).Select(c => new { c.Name, c.ColorSlot }).ToListAsync();
            if (existing.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "A category with this name already exists.");
            }
            // The lowest palette slot no category of this user holds, so colours stay distinct for as long as possible.
            var usedSlots = existing.Select(c => c.ColorSlot).ToHashSet();
            var colorSlot = 0;
            while (usedSlots.Contains(colorSlot))
            {
                colorSlot++;
            }
            var category = new Category
            {
                Id = Guid.NewGuid(),
                Name = name,
                Kind = kind,
                SortOrder = NewCategorySortOrder,
                ColorSlot = colorSlot,
                UserId = userId,
            };
            db.Categories.Add(category);
            await db.SaveChangesAsync();
            return Results.Created($"/api/categorization/categories/{category.Id}", ToDto(category));
        })
        .AddEndpointFilter(RequireValidAntiforgery);
        categorization.MapDelete("/categories/{id:guid}", async (
            Guid id,
            bool? uncategorizeTransactions,
            AppDbContext db,
            UserManager<AppUser> userManager,
            ClaimsPrincipal principal) =>
        {
            var userId = principal.GetUserId(userManager);
            var category = await db.Categories.OwnedBy(userId).FirstOrDefaultAsync(c => c.Id == id);
            if (category is null)
            {
                return Results.NotFound();
            }
            var linkedTransactions = await db.Transactions.Where(t => t.UserId == userId && t.CategoryId == id).ToListAsync();
            if (linkedTransactions.Count > 0 && uncategorizeTransactions != true)
            {
                // The client shows transactionCount in a warning and re-sends with
                // uncategorizeTransactions=true once the user has accepted that those
                // transactions go back to the categorization queue.
                return Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "This category is used by transactions.",
                    extensions: new Dictionary<string, object?> { ["transactionCount"] = linkedTransactions.Count });
            }
            foreach (var transaction in linkedTransactions)
            {
                transaction.CategoryId = null;
                transaction.Category = null;
            }
            db.Categories.Remove(category);
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // Defense-in-depth for a real relational database: a transaction categorized
                // concurrently between the load above and this save still hits the FK constraint.
                return Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "This category changed while it was being deleted. Please try again.");
            }
            return Results.NoContent();
        })
        .AddEndpointFilter(RequireValidAntiforgery);
        categorization.MapGet("/queue/count", async (
            AppDbContext db,
            TransferDetectionService transferDetection,
            UserManager<AppUser> userManager,
            ClaimsPrincipal principal) =>
        {
            var userId = principal.GetUserId(userManager);
            // Same detection pass as /queue so both always agree on what is waiting.
            await transferDetection.DetectAsync(userId, db);
            var count = await db.Transactions.CountAsync(t => t.UserId == userId && t.CategoryId == null && !t.IsInternalTransfer);
            return Results.Ok(new UncategorizedCountDto(count));
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
                var categoryExists = await db.Categories.OwnedBy(userId).AnyAsync(c => c.Id == request.CategoryId);
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

    private static CategoryDto ToDto(Category c) =>
        new(c.Id, c.Name, c.Kind == CategoryKind.Income ? "income" : "expense", c.ColorSlot);

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
}