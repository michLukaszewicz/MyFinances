using Microsoft.EntityFrameworkCore;
using MyFinances.Api.Transactions;

namespace MyFinances.Api.Categorization;

// Auto-flags internal-transfer pairs (FR-009) the user hasn't already manually decided on.
// Runs inline on every queue/handled-list GET (see CategorizationEndpoints) rather than as a
// standalone/background pass, so it never needs to hook into the import-commit path owned by
// another module (see Account.cs:4-5's hand-off comment).
public class TransferDetectionService
{
    // A small tolerance for the two accounts' banks posting the same transfer on slightly
    // different calendar days — the PRD doesn't specify a window, so this is a documented
    // assumption (see plan.md's Critical Implementation Details).
    private const int TransferDateToleranceDays = 2;

    public async Task DetectAsync(Guid userId, AppDbContext db)
    {
        // Only candidates the user hasn't manually decided on are eligible; a manually-set
        // flag must never be touched by an automatic detection pass.
        var candidates = await db.Transactions
            .Where(t => t.UserId == userId && !t.TransferFlagManuallySet)
            .OrderBy(t => t.Date).ThenBy(t => t.Id)
            .ToListAsync();

        var matched = new HashSet<Guid>();

        for (var i = 0; i < candidates.Count; i++)
        {
            var a = candidates[i];
            if (matched.Contains(a.Id))
            {
                continue;
            }

            for (var j = i + 1; j < candidates.Count; j++)
            {
                var b = candidates[j];
                if (matched.Contains(b.Id))
                {
                    continue;
                }

                if (IsTransferPair(a, b))
                {
                    matched.Add(a.Id);
                    matched.Add(b.Id);
                    a.IsInternalTransfer = true;
                    b.IsInternalTransfer = true;
                    break;
                }
            }
        }

        if (matched.Count > 0)
        {
            await db.SaveChangesAsync();
        }
    }

    // Illustrative match predicate from the plan, made concrete: two transactions for the
    // same user are one internal-transfer pair when all of: different known account, exactly
    // opposite amount, and within the date tolerance above.
    private static bool IsTransferPair(Transaction a, Transaction b) =>
        a.AccountId != b.AccountId &&
        a.Amount == -b.Amount &&
        Math.Abs(a.Date.DayNumber - b.Date.DayNumber) <= TransferDateToleranceDays;
}
