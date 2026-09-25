using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyFinances.Api.Transactions;

namespace MyFinances.Api.Import;

public static class ImportEndpoints
{
    // Nested under the /api group so /api/import/* inherits RequireAuthorization() — import
    // always requires a session, unlike /api/auth/register|login which opt out.
    public static void MapImportEndpoints(this IEndpointRouteBuilder api)
    {
        var import = api.MapGroup("/import");

        import.MapPost("/parse", async (
            IFormFile file,
            [FromForm] string? bank,
            IEnumerable<IBankStatementParser> parsers,
            AppDbContext db,
            UserManager<AppUser> userManager,
            ClaimsPrincipal principal) =>
        {
            await using var stream = new MemoryStream();
            await file.CopyToAsync(stream);
            stream.Position = 0;

            // CanParse resets a seekable stream's position after scanning, so trying every
            // registered parser in turn is safe — each gets to inspect the same fresh bytes.
            var parser = parsers.FirstOrDefault(p => p.CanParse(stream));

            if (parser is null && !string.IsNullOrWhiteSpace(bank))
            {
                // Manual-selection fallback: auto-detection failed, but the caller told us
                // which bank this is. Trust the explicit choice instead of erroring.
                parser = parsers.FirstOrDefault(p => string.Equals(p.BankName, bank, StringComparison.OrdinalIgnoreCase));
            }

            if (parser is null)
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Could not recognize this file's bank format. Select a bank manually and retry.");
            }

            stream.Position = 0;
            var parseResult = parser.Parse(stream);

            var userId = principal.GetUserId(userManager);

            var hashes = parseResult.Transactions
                .Select(t => DedupHash.ComputeHash(userId, t.Date, t.Amount, t.Description, parser.BankName))
                .ToList();

            var existingByHash = await db.Transactions
                .Where(t => t.UserId == userId && hashes.Contains(t.Hash))
                .ToDictionaryAsync(t => t.Hash, t => t);

            var rows = parseResult.Transactions.Select(t =>
            {
                var hash = DedupHash.ComputeHash(userId, t.Date, t.Amount, t.Description, parser.BankName);
                existingByHash.TryGetValue(hash, out var existing);

                return new ImportParseRow(
                    t.Date,
                    t.Description,
                    t.Amount,
                    existing is not null,
                    existing is null ? null : new ExistingTransactionDto(existing.Date, existing.Description, existing.Amount));
            }).ToList();

            return Results.Ok(new ImportParseResponse(parser.BankName, rows, parseResult.SkippedErrorCount));
        });

        import.MapPost("/commit", async (
            ImportCommitRequest request,
            IEnumerable<IBankStatementParser> parsers,
            AppDbContext db,
            UserManager<AppUser> userManager,
            ClaimsPrincipal principal) =>
        {
            // The server is not a trust boundary on data it re-receives from its own prior
            // response: every row is re-validated here rather than trusting client-supplied
            // hashes or IsDuplicate flags.
            var parser = parsers.FirstOrDefault(p => string.Equals(p.BankName, request.Bank, StringComparison.OrdinalIgnoreCase));
            if (parser is null)
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Unknown bank.");
            }

            var userId = principal.GetUserId(userManager);

            var keepRows = request.Rows.Where(r => r.Decision == RowDecision.Keep).ToList();
            var keepHashes = keepRows
                .Select(r => DedupHash.ComputeHash(userId, r.Date, r.Amount, r.Description, parser.BankName))
                .ToList();

            var skipRows = request.Rows.Where(r => r.Decision == RowDecision.Skip).ToList();
            var skipHashes = skipRows
                .Select(r => DedupHash.ComputeHash(userId, r.Date, r.Amount, r.Description, parser.BankName))
                .ToList();

            var allHashes = keepHashes.Concat(skipHashes).ToList();
            var existingHashes = await db.Transactions
                .Where(t => t.UserId == userId && allHashes.Contains(t.Hash))
                .Select(t => t.Hash)
                .ToHashSetAsync();

            var importBatch = new ImportBatch
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Bank = parser.BankName,
                ImportedAtUtc = DateTime.UtcNow,
                ImportedCount = keepRows.Count,
                SkippedDuplicateCount = skipHashes.Count(h => existingHashes.Contains(h)),
                SkippedErrorCount = request.SkippedErrorCount,
            };

            db.ImportBatches.Add(importBatch);

            for (var i = 0; i < keepRows.Count; i++)
            {
                var row = keepRows[i];
                var hash = keepHashes[i];

                // A "Keep" decision means insert regardless of duplicate status — the user
                // explicitly chose to keep a colliding row. The hash is still recomputed
                // server-side (never trusted from the client) for storage.
                db.Transactions.Add(new Transaction
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Bank = parser.BankName,
                    Date = row.Date,
                    Description = row.Description,
                    Amount = row.Amount,
                    Hash = hash,
                    ImportBatchId = importBatch.Id,
                });
            }

            await db.SaveChangesAsync();

            return Results.Ok(new ImportSummaryDto(
                importBatch.Id,
                importBatch.Bank,
                importBatch.ImportedAtUtc,
                importBatch.ImportedCount,
                importBatch.SkippedDuplicateCount,
                importBatch.SkippedErrorCount));
        })
        .AddEndpointFilter(async (context, next) =>
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
        });
    }
}
