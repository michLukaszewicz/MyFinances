using Microsoft.IdentityModel.Tokens;
using MyFinancesAPI.Models.Identity;
using MyFinancesAPI.Services.Abstractions;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace MyFinancesAPI.Services
{
    public class JwtProvider : IJwtProvider
    {
        private readonly IConfiguration configuration;

        public JwtProvider(IConfiguration configuration)
        {
            this.configuration = configuration;
        }

        public string GetJwtTokenForUser(DateTimeOffset expireTimeOffset, User user)
        {
            var claims = new List<Claim>()
                {
                    new Claim(ClaimTypes.Name, user.FirstName),
                    new Claim(ClaimTypes.Email, user.Email!),
                    new Claim("User", "true"),
                };
            return GetJwtToken(claims, expireTimeOffset.UtcDateTime);
        }

        private string GetJwtToken(IEnumerable<Claim> claims, DateTime expireTime)
        {
            var jwt = new JwtSecurityToken(
                claims: claims,
                notBefore: DateTime.UtcNow,
                expires: expireTime,
                signingCredentials: new SigningCredentials(
                    new SymmetricSecurityKey(Encoding.ASCII.GetBytes(configuration["Jwt:Secret"] ?? String.Empty)),
                    SecurityAlgorithms.HmacSha256Signature));

            return new JwtSecurityTokenHandler().WriteToken(jwt);
        }
    }
}