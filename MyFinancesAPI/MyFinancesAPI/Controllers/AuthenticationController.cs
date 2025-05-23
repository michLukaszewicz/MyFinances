using Microsoft.AspNetCore.Mvc;
using MyFinancesAPI.Models.Identity;
using MyFinancesAPI.Services;
using System.Security.Claims;

namespace MyFinancesAPI.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class AuthenticationController : ControllerBase
    {
        private readonly IJwtProvider jwtProvider;

        public AuthenticationController(IJwtProvider jwtProvider)
        {
            this.jwtProvider = jwtProvider;
        }

        [HttpPost]
        public IActionResult Authenticate([FromBody] Credential credential)
        {
            if (credential.UserName == "admin" && credential.Password == "password")
            {
                var claims = new List<Claim>()
                {
                    new Claim(ClaimTypes.Name, "admin"),
                    new Claim(ClaimTypes.Email, "admin@email.com"),
                    new Claim("Admin", "true"),
                    new Claim("User", "true"),
                };
                var expireTime = DateTimeOffset.UtcNow.AddMinutes(3);

                return Ok(new
                {
                    access_token = jwtProvider.GetJwtToken(claims, expireTime.UtcDateTime),
                    expires_at = expireTime,
                });
            }
            ModelState.AddModelError("Unauthorized", "Wrong credentials");
            return Unauthorized(ModelState);
        }
    }
}