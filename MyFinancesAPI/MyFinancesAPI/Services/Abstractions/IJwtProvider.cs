using MyFinancesAPI.Models.Identity;

namespace MyFinancesAPI.Services.Abstractions
{
    public interface IJwtProvider
    {
        string GetJwtTokenForUser(DateTimeOffset expireTimeOffset, User user);
    }
}