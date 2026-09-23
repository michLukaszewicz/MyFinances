using Microsoft.AspNetCore.Identity;

namespace MyFinances.Api;

public class AppUser : IdentityUser<Guid>
{
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
