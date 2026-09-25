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

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Every parse/commit does a per-user hash lookup to detect duplicates; this index
        // is deliberately non-unique (see Transaction.Hash).
        builder.Entity<Transaction>()
            .HasIndex(t => new { t.UserId, t.Hash });
    }
}
