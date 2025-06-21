using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using MyFinancesAPI.Models.Identity;
using MyFinancesAPI.Services;
using System.Security.Claims;

namespace MyFinancesAPI.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class AuthenticationController(IJwtProvider jetProvider, UserManager<User> userManager) : ControllerBase
    {
        private readonly IJwtProvider jwtProvider = jetProvider;
        private readonly UserManager<User> userManager = userManager;

        [HttpPost]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(object))]
        [ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ModelStateDictionary))]
        public IActionResult Authenticate([FromBody] LoginDto credential)
        {
            if (credential.UserName == "admin" && credential.Password == "password")
            {
                var claims = new List<Claim>()
                {
                    new Claim(ClaimTypes.Name, "admen"),
                    new Claim(ClaimTypes.Email, "admin@email.com"),
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

        [HttpPost]
        public async Task<IActionResult> Register([FromBody] RegisterDto registerDto)
        {
            if (IsDtoInvalid(registerDto))
            {
                ModelState.AddModelError("NullProperties", "All fields are required.");
                return BadRequest(ModelState);
            }

            var user = new User
            {
                FirstName = registerDto.FirstName!,
                UserName = registerDto.Email,
                Email = registerDto.Email,
            };

            IdentityResult result = await userManager.CreateAsync(user, registerDto.Password!);

            if (result.Succeeded)
            {
                return Ok();
            }

            string[] errorDescriptions = result.Errors.Select(error => error.Description).ToArray();
            return BadRequest(new { Errors = errorDescriptions });
        }

        private static bool IsDtoInvalid(RegisterDto registerDto) =>
            registerDto.FirstName is null ||
            registerDto.Email is null ||
            registerDto.Password is null ||
            registerDto.ConfirmPassword is null ||
            registerDto.Password != registerDto.ConfirmPassword;
    }
}