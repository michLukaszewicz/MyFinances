using System.Data;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MyFinances.Api.Categorization;
using MyFinances.Api.Import;

namespace MyFinances.Api.Transactions;

public static class TransactionEndpoints
{
    private const int DefaultTake = 20;
    private const int MaxTake = 100;

    // Nested under the /api group so /api/transactions inherits RequireAuthorization() — no
    // explicit attribute needed. GET has no antiforgery filter; POST/PUT/DELETE each add
    // RequireValidAntiforgery, per ImportEndpoints.cs's / AccountEndpoints.cs's convention.
    public static void MapTransactionEndpoints(this IEndpointRouteBuilder api)
    {
        var transactions = api.MapGroup("/transactions");

        transactions.MapGet("/", async (
            int? skip,
            int? take,
            Guid? categoryId,
            bool? currentMonth,
            string? kind,
            AppDbContext db,
            UserManager<AppUser> userManager,
            ClaimsPrincipal principal) =>
        {
            var userId = principal.GetUserId(userManager);

            var effectiveSkip = Math.Max(skip ?? 0, 0);
            var effectiveTake = Math.Clamp(take ?? DefaultTake, 1, MaxTake);

            var query = db.Transactions.Where(t => t.UserId == userId);

            if (categoryId is not null)
            {
                query = query.Where(t => t.CategoryId == categoryId);
            }

            if (currentMonth == true)
            {
                var range = CurrentMonthRange.Get();
                query = query.Where(t => t.Date >= range.Start && t.Date <= range.End);
            }

            // Drilling into a chart slice (category + current month) must list exactly what the
            // slice summed (see DashboardEndpoints): non-transfer rows of the matching sign —
            // positive for the income chart (kind=income), negative otherwise.
            if (categoryId is not null && currentMonth == true)
            {
                query = string.Equals(kind, "income", StringComparison.OrdinalIgnoreCase)
                    ? query.Where(t => t.Amount > 0 && !t.IsInternalTransfer)
                    : query.Where(t => t.Amount < 0 && !t.IsInternalTransfer);
            }

            // No CreatedAt field exists, and Date alone isn't unique per user, so Id is the
            // deterministic tiebreak for a stable newest-first ordering across pages.
            var items = await query
                .OrderByDescending(t => t.Date)
                .ThenByDescending(t => t.Id)
                .Skip(effectiveSkip)
                .Take(effectiveTake)
                .Select(t => new TransactionListItemDto(
                    t.Id,
                    t.Date,
                    t.Description,
                    t.Amount,
                    t.CategoryId,
                    db.Categories.Where(c => c.Id == t.CategoryId).Select(c => c.Name).FirstOrDefault(),
                    t.AccountId,
                    t.IsInternalTransfer))
                .ToListAsync();

            var totalCount = await query.CountAsync();
            var hasMore = effectiveSkip + items.Count < totalCount;

            return Results.Ok(new TransactionListResponseDto(items, hasMore));
        });

        transactions.MapPost("/", async (
            TransactionWriteRequest request,
            AppDbContext db,
            UserManager<AppUser> userManager,
            ClaimsPrincipal principal) =>
        {
            var userId = principal.GetUserId(userManager);

            var accountOwned = await db.Accounts.AnyAsync(a => a.Id == request.AccountId && a.UserId == userId);
            if (!accountOwned)
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Unknown account.");
            }

            var categoryExists = await db.Categories.AnyAsync(c => c.Id == request.CategoryId);
            if (!categoryExists)
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Unknown category.");
            }

            var hash = DedupHash.ComputeHash(userId, request.Date, request.Amount, request.Description, request.AccountId);

            // Serializable transaction so the duplicate-hash check and the insert are atomic
            // against a concurrent identical request — see BeginTransactionIfSupportedAsync for
            // why this is best-effort rather than unconditional.
            await using var dbTransaction = await BeginTransactionIfSupportedAsync(db);

            if (!request.Force)
            {
                var duplicate = await db.Transactions
                    .Where(t => t.UserId == userId && t.Hash == hash)
                    .Select(t => new ExistingTransactionDto(t.Date, t.Description, t.Amount))
                    .FirstOrDefaultAsync();
                if (duplicate is not null)
                {
                    return Results.Json(
                        new DuplicateTransactionResponse("A transaction with matching details already exists.", duplicate),
                        statusCode: StatusCodes.Status409Conflict);
                }
            }

            var transaction = new Transaction
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                AccountId = request.AccountId,
                Date = request.Date,
                Description = request.Description,
                Amount = request.Amount,
                Hash = hash,
                CategoryId = request.CategoryId,
                ImportBatchId = null,
            };

            db.Transactions.Add(transaction);
            await db.SaveChangesAsync();

            if (dbTransaction is not null)
            {
                await dbTransaction.CommitAsync();
            }

            var categoryName = await db.Categories.Where(c => c.Id == request.CategoryId).Select(c => c.Name).FirstAsync();

            return Results.Created(
                $"/api/transactions/{transaction.Id}",
                new TransactionDetailDto(transaction.Id, transaction.Date, transaction.Description, transaction.Amount, transaction.AccountId, transaction.CategoryId!.Value, categoryName));
        })
        .AddEndpointFilter(RequireValidAntiforgery);

        transactions.MapPut("/{id:guid}", async (
            Guid id,
            TransactionWriteRequest request,
            AppDbContext db,
            UserManager<AppUser> userManager,
            ClaimsPrincipal principal) =>
        {
            var userId = principal.GetUserId(userManager);

            var transaction = await db.Transactions.FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId);
            if (transaction is null)
            {
                return Results.NotFound();
            }

            var accountOwned = await db.Accounts.AnyAsync(a => a.Id == request.AccountId && a.UserId == userId);
            if (!accountOwned)
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Unknown account.");
            }

            var categoryExists = await db.Categories.AnyAsync(c => c.Id == request.CategoryId);
            if (!categoryExists)
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Unknown category.");
            }

            var hash = DedupHash.ComputeHash(userId, request.Date, request.Amount, request.Description, request.AccountId);

            // Serializable transaction so the duplicate-hash check and the update are atomic
            // against a concurrent identical request — see BeginTransactionIfSupportedAsync for
            // why this is best-effort rather than unconditional.
            await using var dbTransaction = await BeginTransactionIfSupportedAsync(db);

            if (!request.Force)
            {
                var duplicate = await db.Transactions
                    .Where(t => t.UserId == userId && t.Hash == hash && t.Id != id)
                    .Select(t => new ExistingTransactionDto(t.Date, t.Description, t.Amount))
                    .FirstOrDefaultAsync();
                if (duplicate is not null)
                {
                    return Results.Json(
                        new DuplicateTransactionResponse("A transaction with matching details already exists.", duplicate),
                        statusCode: StatusCodes.Status409Conflict);
                }
            }

            // Hash is recomputed from the new field values so future duplicate detection reflects
            // this row's current data, not its original (e.g. import-time) snapshot. ImportBatchId
            // is deliberately left untouched.
            transaction.Date = request.Date;
            transaction.Description = request.Description;
            transaction.Amount = request.Amount;
            transaction.AccountId = request.AccountId;
            transaction.CategoryId = request.CategoryId;
            transaction.Hash = hash;

            await db.SaveChangesAsync();

            if (dbTransaction is not null)
            {
                await dbTransaction.CommitAsync();
            }

            var categoryName = await db.Categories.Where(c => c.Id == request.CategoryId).Select(c => c.Name).FirstAsync();

            return Results.Ok(new TransactionDetailDto(transaction.Id, transaction.Date, transaction.Description, transaction.Amount, transaction.AccountId, transaction.CategoryId!.Value, categoryName));
        })
        .AddEndpointFilter(RequireValidAntiforgery);

        transactions.MapDelete("/{id:guid}", async (
            Guid id,
            AppDbContext db,
            UserManager<AppUser> userManager,
            ClaimsPrincipal principal) =>
        {
            var userId = principal.GetUserId(userManager);

            var transaction = await db.Transactions.FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId);
            if (transaction is null)
            {
                return Results.NotFound();
            }

            // Unconditional hard delete for any transaction the caller owns, imported or manual
            // alike — nothing else in the schema references Transaction.Id as a foreign key.
            db.Transactions.Remove(transaction);
            await db.SaveChangesAsync();

            return Results.NoContent();
        })
        .AddEndpointFilter(RequireValidAntiforgery);
    }

    // Best-effort: the EF Core InMemory provider used by the test suite (see
    // Tests/AuthApiFactory.cs) doesn't support transactions and throws InvalidOperationException
    // from BeginTransactionAsync. Production (Npgsql) supports it, so Serializable actually
    // closes the duplicate-hash check-then-write race there; tests fall back to no transaction,
    // which is fine since they don't exercise concurrent requests.
    private static async ValueTask<IDbContextTransaction?> BeginTransactionIfSupportedAsync(AppDbContext db)
    {
        try
        {
            return await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
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
}
