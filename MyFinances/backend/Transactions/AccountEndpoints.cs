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

            db.Accounts.Remove(account);
            await db.SaveChangesAsync();

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
