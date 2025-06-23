using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using MyFinancesAPI.Models.Identity;
using MyFinancesAPI.Services.Abstractions;
using SignInResult = Microsoft.AspNetCore.Identity.SignInResult;

namespace MyFinancesAPI.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class AuthenticationController(IJwtProvider jetProvider, UserManager<User> userManager, SignInManager<User> signInManager) : ControllerBase
    {
        private readonly IJwtProvider jwtProvider = jetProvider;
        private readonly SignInManager<User> signInManager = signInManager;
        private readonly UserManager<User> userManager = userManager;
        private static DateTimeOffset DefaultExpireTime => DateTimeOffset.UtcNow.AddMinutes(3);

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
                string token = jwtProvider.GetJwtTokenForUser(DefaultExpireTime, user);
                return Ok(new
                {
                    access_token = token,
                    expires_at = DefaultExpireTime,
                });
            }
            ModelState.AddModelError("Unauthorized", "Invalid credentials");
            return Unauthorized(ModelState);
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterDto registerDto)
        {
            if (!IsDtoValid(registerDto))
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

        private static bool IsDtoValid(RegisterDto registerDto) =>
            !(registerDto.FirstName is not null &&
              registerDto.Email is not null &&
              registerDto.Password is not null &&
              registerDto.ConfirmPassword is not null &&
              registerDto.Password == registerDto.ConfirmPassword);
    }
}