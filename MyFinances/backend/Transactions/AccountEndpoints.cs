using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MyFinances.Api.Import;

namespace MyFinances.Api.Transactions;

public static class AccountEndpoints
{
    // Nested under the /api group so /api/accounts/* inherits RequireAuthorization(),
    // matching ImportEndpoints' convention.
    public static void MapAccountEndpoints(this IEndpointRouteBuilder api)
    {
        var accounts = api.MapGroup("/accounts");

        accounts.MapGet("/banks", (IEnumerable<IBankStatementParser> parsers) =>
        {
            var bankNames = parsers
                .Select(p => p.BankName)
                .Distinct()
                .Append("Other")
                .ToList();

            return Results.Ok(new BankOptionsResponse(bankNames));
        });

        accounts.MapGet("/", async (
            AppDbContext db,
            UserManager<AppUser> userManager,
            ClaimsPrincipal principal) =>
        {
            var userId = principal.GetUserId(userManager);

            var result = await db.Accounts
                .Where(a => a.UserId == userId)
                .OrderBy(a => a.BankName).ThenBy(a => a.AccountNumber)
                .Select(a => new AccountDto(a.Id, a.BankName, a.AccountNumber))
                .ToListAsync();

            return Results.Ok(result);
        });

        accounts.MapPost("/", async (
            AccountWriteRequest request,
            AppDbContext db,
            UserManager<AppUser> userManager,
            ClaimsPrincipal principal) =>
        {
            var userId = principal.GetUserId(userManager);

            var isDuplicate = await db.Accounts.AnyAsync(a =>
                a.UserId == userId && a.BankName == request.BankName && a.AccountNumber == request.AccountNumber);
            if (isDuplicate)
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "An account with this bank and account number already exists.");
            }

            var account = new Account
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                BankName = request.BankName,
                AccountNumber = request.AccountNumber,
            };

            db.Accounts.Add(account);
            await db.SaveChangesAsync();

            return Results.Created($"/api/accounts/{account.Id}", new AccountDto(account.Id, account.BankName, account.AccountNumber));
        })
        .AddEndpointFilter(RequireValidAntiforgery);

        accounts.MapPut("/{id:guid}", async (
            Guid id,
            AccountWriteRequest request,
            AppDbContext db,
            UserManager<AppUser> userManager,
            ClaimsPrincipal principal) =>
        {
            var userId = principal.GetUserId(userManager);

            var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId);
            if (account is null)
            {
                return Results.NotFound();
            }

            var collidesWithDifferentAccount = await db.Accounts.AnyAsync(a =>
                a.UserId == userId && a.Id != id && a.BankName == request.BankName && a.AccountNumber == request.AccountNumber);
            if (collidesWithDifferentAccount)
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "An account with this bank and account number already exists.");
            }

            account.BankName = request.BankName;
            account.AccountNumber = request.AccountNumber;
            await db.SaveChangesAsync();

            return Results.Ok(new AccountDto(account.Id, account.BankName, account.AccountNumber));
        })
        .AddEndpointFilter(RequireValidAntiforgery);

        accounts.MapDelete("/{id:guid}", async (
            Guid id,
            AppDbContext db,
            UserManager<AppUser> userManager,
            ClaimsPrincipal principal) =>
        {
            var userId = principal.GetUserId(userManager);

            var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId);
            if (account is null)
            {
                return Results.NotFound();
            }

            // Checked proactively (matching the duplicate-account 409 pattern above) rather than
            // relying solely on catching the FK-restrict violation: the linked Transaction/
            // ImportBatch rows are not necessarily tracked by this DbContext instance, so a
            // provider that only detects severed *tracked* required relationships (e.g. EF Core's
            // InMemory provider, used in tests) would otherwise let the delete through silently.
            var hasLinkedHistory = await db.Transactions.AnyAsync(t => t.AccountId == id)
                || await db.ImportBatches.AnyAsync(b => b.AccountId == id);
            if (hasLinkedHistory)
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "This account has linked transactions and cannot be deleted.");
            }

            db.Accounts.Remove(account);
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // Defense-in-depth for a real relational database (Npgsql): a transaction linked
                // concurrently between the check above and this save still hits the FK constraint.
                return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "This account has linked transactions and cannot be deleted.");
            }

            return Results.NoContent();
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
}
