using Microsoft.EntityFrameworkCore;

namespace MyFinances.Api;

// Placeholder context: verifies connectivity to Postgres (Neon) at startup.
// Domain models (transactions, categories, ...) are added with the real feature implementation.
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
}
