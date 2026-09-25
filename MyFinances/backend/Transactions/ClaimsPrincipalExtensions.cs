using System.Security.Claims;
using Microsoft.AspNetCore.Identity;

namespace MyFinances.Api.Transactions;

// Establishes the one convention every domain (per-user) query should follow: resolve the
// current user's id the same way ASP.NET Identity resolves it internally
// (UserManager<TUser>.GetUserId), then scope the query with .Where(x => x.UserId == id).
public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal user, UserManager<AppUser> userManager)
    {
        var id = userManager.GetUserId(user);
        if (id is null)
        {
            throw new InvalidOperationException("No authenticated user id found on the current ClaimsPrincipal.");
        }

        return Guid.Parse(id);
    }
}
