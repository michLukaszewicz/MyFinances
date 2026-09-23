using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace MyFinances.Api;

// Identity context (roles-less/flat user model): backs UserManager/SignInManager with
// AspNetUsers, claims, logins, and tokens tables. No AspNetRoles table is created.
// Domain models (transactions, categories, ...) are added with the real feature implementation.
public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityUserContext<AppUser, Guid>(options)
{
}
