using System.Security.Claims;

namespace MyFinancesAPI.Services
{
    public interface IJwtProvider
    {
        string GetJwtToken(IEnumerable<Claim> claims, DateTime expireTime);
    }
}