using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using MyFinances.Api.Categorization;
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

    public DbSet<Category> Categories => Set<Category>();

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

        // Category is a fixed, seeded, non-user-owned list (no category-management FR yet);
        // deleting one should fail loudly rather than silently orphan transactions.
        builder.Entity<Transaction>()
            .HasOne(t => t.Category)
            .WithMany()
            .HasForeignKey(t => t.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        // Deterministic, hard-coded Guids so the seed is stable across environments/migrations.
        builder.Entity<Category>().HasData(
            new Category { Id = new Guid("00000000-0000-0000-0000-000000000001"), Name = "Groceries", SortOrder = 1 },
            new Category { Id = new Guid("00000000-0000-0000-0000-000000000002"), Name = "Dining & Takeout", SortOrder = 2 },
            new Category { Id = new Guid("00000000-0000-0000-0000-000000000003"), Name = "Transport", SortOrder = 3 },
            new Category { Id = new Guid("00000000-0000-0000-0000-000000000004"), Name = "Housing & Utilities", SortOrder = 4 },
            new Category { Id = new Guid("00000000-0000-0000-0000-000000000005"), Name = "Health", SortOrder = 5 },
            new Category { Id = new Guid("00000000-0000-0000-0000-000000000006"), Name = "Shopping", SortOrder = 6 },
            new Category { Id = new Guid("00000000-0000-0000-0000-000000000007"), Name = "Entertainment", SortOrder = 7 },
            new Category { Id = new Guid("00000000-0000-0000-0000-000000000008"), Name = "Travel", SortOrder = 8 },
            new Category { Id = new Guid("00000000-0000-0000-0000-000000000009"), Name = "Subscriptions", SortOrder = 9 },
            new Category { Id = new Guid("00000000-0000-0000-0000-000000000010"), Name = "Income", SortOrder = 10 },
            new Category { Id = new Guid("00000000-0000-0000-0000-000000000011"), Name = "Fees & Charges", SortOrder = 11 },
            new Category { Id = new Guid("00000000-0000-0000-0000-000000000012"), Name = "Other", SortOrder = 12 }
        );
    }
}
