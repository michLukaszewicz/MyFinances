using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using MyFinances.Api.Transactions;

namespace MyFinances.Api;

// Identity context (roles-less/flat user model): backs UserManager/SignInManager with
// AspNetUsers, claims, logins, and tokens tables. No AspNetRoles table is created.
// Domain models (transactions, categories, ...) are added with the real feature implementation.
public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityUserContext<AppUser, Guid>(options)
{
    public DbSet<Transaction> Transactions => Set<Transaction>();

    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();

    public DbSet<Account> Accounts => Set<Account>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Every parse/commit does a per-user hash lookup to detect duplicates; this index
        // is deliberately non-unique (see Transaction.Hash).
        builder.Entity<Transaction>()
            .HasIndex(t => new { t.UserId, t.Hash });

        // Every transaction/import batch belongs to exactly one account; deleting an account
        // with existing history should fail loudly rather than cascade-delete it.
        builder.Entity<Transaction>()
            .HasOne(t => t.Account)
            .WithMany()
            .HasForeignKey(t => t.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<ImportBatch>()
            .HasOne(b => b.Account)
            .WithMany()
            .HasForeignKey(b => b.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        // Enforces "no duplicate bank+number per user" at the DB level; endpoints translate
        // a violation into a friendly 409 instead of letting a raw DbUpdateException surface.
        builder.Entity<Account>()
            .HasIndex(a => new { a.UserId, a.BankName, a.AccountNumber })
            .IsUnique();
    }
}
