using Microsoft.EntityFrameworkCore;
using MyFinances.Api.Categorization;
using MyFinances.Api.Transactions;
using Xunit;

namespace MyFinances.Api.Tests;

// Unit coverage for TransferDetectionService's match predicate, isolated from the HTTP layer
// via a dedicated EF Core InMemory AppDbContext per test (fresh Guid database name each time).
public class TransferDetectionServiceTests
{
    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static Transaction MakeTransaction(Guid userId, Guid accountId, DateOnly date, decimal amount, string description = "txn") => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        AccountId = accountId,
        Date = date,
        Description = description,
        Amount = amount,
        Hash = Guid.NewGuid().ToString(),
    };

    [Fact]
    public async Task DetectAsync_FlagsBothLegs_ForSameUserOppositeAccountOppositeAmountWithinTolerance()
    {
        using var db = CreateDb();
        var userId = Guid.NewGuid();
        var accountA = Guid.NewGuid();
        var accountB = Guid.NewGuid();

        var t1 = MakeTransaction(userId, accountA, new DateOnly(2026, 8, 1), -500.00m);
        var t2 = MakeTransaction(userId, accountB, new DateOnly(2026, 8, 2), 500.00m);
        db.Transactions.AddRange(t1, t2);
        await db.SaveChangesAsync();

        await new TransferDetectionService().DetectAsync(userId, db);

        var reloaded1 = await db.Transactions.FindAsync(t1.Id);
        var reloaded2 = await db.Transactions.FindAsync(t2.Id);
        Assert.True(reloaded1!.IsInternalTransfer);
        Assert.True(reloaded2!.IsInternalTransfer);
    }

    [Fact]
    public async Task DetectAsync_DoesNotFlag_WhenTransactionsBelongToDifferentUsers()
    {
        using var db = CreateDb();
        var accountA = Guid.NewGuid();
        var accountB = Guid.NewGuid();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        var t1 = MakeTransaction(userA, accountA, new DateOnly(2026, 8, 1), -500.00m);
        var t2 = MakeTransaction(userB, accountB, new DateOnly(2026, 8, 1), 500.00m);
        db.Transactions.AddRange(t1, t2);
        await db.SaveChangesAsync();

        await new TransferDetectionService().DetectAsync(userA, db);
        await new TransferDetectionService().DetectAsync(userB, db);

        var reloaded1 = await db.Transactions.FindAsync(t1.Id);
        var reloaded2 = await db.Transactions.FindAsync(t2.Id);
        Assert.False(reloaded1!.IsInternalTransfer);
        Assert.False(reloaded2!.IsInternalTransfer);
    }

    [Fact]
    public async Task DetectAsync_DoesNotFlag_WhenSameAccount()
    {
        using var db = CreateDb();
        var userId = Guid.NewGuid();
        var accountA = Guid.NewGuid();

        var t1 = MakeTransaction(userId, accountA, new DateOnly(2026, 8, 1), -500.00m);
        var t2 = MakeTransaction(userId, accountA, new DateOnly(2026, 8, 1), 500.00m);
        db.Transactions.AddRange(t1, t2);
        await db.SaveChangesAsync();

        await new TransferDetectionService().DetectAsync(userId, db);

        var reloaded1 = await db.Transactions.FindAsync(t1.Id);
        var reloaded2 = await db.Transactions.FindAsync(t2.Id);
        Assert.False(reloaded1!.IsInternalTransfer);
        Assert.False(reloaded2!.IsInternalTransfer);
    }

    [Fact]
    public async Task DetectAsync_DoesNotFlag_WhenAmountsAreNotExactOpposites()
    {
        using var db = CreateDb();
        var userId = Guid.NewGuid();
        var accountA = Guid.NewGuid();
        var accountB = Guid.NewGuid();

        var t1 = MakeTransaction(userId, accountA, new DateOnly(2026, 8, 1), -500.00m);
        var t2 = MakeTransaction(userId, accountB, new DateOnly(2026, 8, 1), 499.00m);
        db.Transactions.AddRange(t1, t2);
        await db.SaveChangesAsync();

        await new TransferDetectionService().DetectAsync(userId, db);

        var reloaded1 = await db.Transactions.FindAsync(t1.Id);
        var reloaded2 = await db.Transactions.FindAsync(t2.Id);
        Assert.False(reloaded1!.IsInternalTransfer);
        Assert.False(reloaded2!.IsInternalTransfer);
    }

    [Fact]
    public async Task DetectAsync_DoesNotFlag_WhenDatesAreOutsideTolerance()
    {
        using var db = CreateDb();
        var userId = Guid.NewGuid();
        var accountA = Guid.NewGuid();
        var accountB = Guid.NewGuid();

        var t1 = MakeTransaction(userId, accountA, new DateOnly(2026, 8, 1), -500.00m);
        var t2 = MakeTransaction(userId, accountB, new DateOnly(2026, 8, 4), 500.00m);
        db.Transactions.AddRange(t1, t2);
        await db.SaveChangesAsync();

        await new TransferDetectionService().DetectAsync(userId, db);

        var reloaded1 = await db.Transactions.FindAsync(t1.Id);
        var reloaded2 = await db.Transactions.FindAsync(t2.Id);
        Assert.False(reloaded1!.IsInternalTransfer);
        Assert.False(reloaded2!.IsInternalTransfer);
    }

    [Fact]
    public async Task DetectAsync_NeverOverwrites_ManuallySetFlag()
    {
        using var db = CreateDb();
        var userId = Guid.NewGuid();
        var accountA = Guid.NewGuid();
        var accountB = Guid.NewGuid();

        var t1 = MakeTransaction(userId, accountA, new DateOnly(2026, 8, 1), -500.00m);
        t1.TransferFlagManuallySet = true;
        t1.IsInternalTransfer = false;
        var t2 = MakeTransaction(userId, accountB, new DateOnly(2026, 8, 1), 500.00m);
        db.Transactions.AddRange(t1, t2);
        await db.SaveChangesAsync();

        await new TransferDetectionService().DetectAsync(userId, db);

        var reloaded1 = await db.Transactions.FindAsync(t1.Id);
        var reloaded2 = await db.Transactions.FindAsync(t2.Id);
        // t1 was manually decided, so it stays excluded from pairing (and unmatched) —
        // t2 has no eligible partner left and stays unflagged too.
        Assert.False(reloaded1!.IsInternalTransfer);
        Assert.False(reloaded2!.IsInternalTransfer);
    }

    [Fact]
    public async Task DetectAsync_FirstMatchWins_WhenMultipleCandidatesShareDateAndAmount()
    {
        using var db = CreateDb();
        var userId = Guid.NewGuid();
        var accountA = Guid.NewGuid();
        var accountB = Guid.NewGuid();
        var accountC = Guid.NewGuid();

        var t1 = MakeTransaction(userId, accountA, new DateOnly(2026, 8, 1), -500.00m);
        var t2 = MakeTransaction(userId, accountB, new DateOnly(2026, 8, 1), 500.00m);
        var t3 = MakeTransaction(userId, accountC, new DateOnly(2026, 8, 1), 500.00m);
        db.Transactions.AddRange(t1, t2, t3);
        await db.SaveChangesAsync();

        await new TransferDetectionService().DetectAsync(userId, db);

        var reloaded1 = await db.Transactions.FindAsync(t1.Id);
        var reloaded2 = await db.Transactions.FindAsync(t2.Id);
        var reloaded3 = await db.Transactions.FindAsync(t3.Id);
        Assert.True(reloaded1!.IsInternalTransfer);
        // Exactly one of t2/t3 is matched with t1; the other stays unflagged (no optimal
        // matching — first-match-wins per plan.md).
        var matchedCount = new[] { reloaded2!.IsInternalTransfer, reloaded3!.IsInternalTransfer }.Count(x => x);
        Assert.Equal(1, matchedCount);
    }
}
