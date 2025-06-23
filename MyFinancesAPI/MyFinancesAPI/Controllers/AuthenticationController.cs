using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using MyFinancesAPI.Models.Identity;
using MyFinancesAPI.Services;
using System.Security.Claims;
using SignInResult = Microsoft.AspNetCore.Identity.SignInResult;

namespace MyFinancesAPI.Controllers
{
    //TOOD: Spit the controller into two separate controllers: AuthenticationController and RegistrationController
    [Route("[controller]")]
    [ApiController]
    public class AuthenticationController(IJwtProvider jetProvider, UserManager<User> userManager, SignInManager<User> signInManager) : ControllerBase
    {
        private static DateTimeOffset DefaultExpireTime => DateTimeOffset.UtcNow.AddMinutes(3);
        private readonly IJwtProvider jwtProvider = jetProvider;
        private readonly SignInManager<User> signInManager = signInManager;
        private readonly UserManager<User> userManager = userManager;

        [HttpPost]
        //TODO: zmienić też we frontend [HttpPost("Login")] and change the name of the method
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Authenticate([FromBody] LoginDto credential)
        {
            if (credential.UserName is null || credential.Password is null)
            {
                ModelState.AddModelError("NullProperties", "All fields are required.");
                return BadRequest(ModelState);
            }

            //TODO: Update names of the credential names in frontend and backend
            var user = await userManager.FindByEmailAsync(credential.UserName);

            if (user is null)
            {
                return Unauthorized("Invalid credentials");
            }

            SignInResult results = await signInManager.CheckPasswordSignInAsync(user, credential.Password, lockoutOnFailure: false);
            if (results.Succeeded)
            {
                string token = GenerateJwtToken(DefaultExpireTime);
                return Ok(new
                {
                    access_token = token,
                    expires_at = DefaultExpireTime,
                });
            }
            ModelState.AddModelError("Unauthorized", "Invalid credentials");
            return Unauthorized(ModelState);
        }

        private string GenerateJwtToken(DateTimeOffset expireTimeOffset)
        {
            var claims = new List<Claim>()
                {
                    new Claim(ClaimTypes.Name, "admen"),
                    new Claim(ClaimTypes.Email, "admin@email.com"),
                    new Claim("User", "true"),
                };
            return jwtProvider.GetJwtToken(claims, expireTimeOffset.UtcDateTime);
        }

        [HttpPost("register")]
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