using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using MyFinances.Api.Categorization;
using MyFinances.Api.Import;
using MyFinances.Api.Transactions;

namespace MyFinances.Api;

// Identity context (roles-less/flat user model): backs UserManager/SignInManager with
// AspNetUsers, claims, logins, and tokens tables. No AspNetRoles table is created.
// Domain models (transactions, categories, ...) are added with the real feature implementation.
public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityUserContext<AppUser, Guid>(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();
    public DbSet<Transaction> Transactions => Set<Transaction>();

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
        // Stored as text ("Csv"/"Pdf"); existing rows default to Csv (the migration backfills Pdf
        // for VeloBank accounts).
        builder.Entity<ImportBatch>()
            .Property(b => b.SourceFormat)
            .HasConversion<string>()
            .HasDefaultValue(StatementFormat.Csv);
        // Enforces "no duplicate bank+number per user" at the DB level; endpoints translate
        // a violation into a friendly 409 instead of letting a raw DbUpdateException surface.
        builder.Entity<Account>()
            .HasIndex(a => new { a.UserId, a.BankName, a.AccountNumber })
            .IsUnique();
        // Deleting a category should fail loudly rather than silently orphan transactions; the
        // category delete endpoint un-categorizes the owner's transactions explicitly first.
        builder.Entity<Transaction>()
            .HasOne(t => t.Category)
            .WithMany()
            .HasForeignKey(t => t.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);
        // Category lists are read per user, so index the owner.
        builder.Entity<Category>()
            .HasIndex(c => c.UserId);
    }
}