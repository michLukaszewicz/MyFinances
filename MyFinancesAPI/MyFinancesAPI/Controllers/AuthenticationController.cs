using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using MyFinancesAPI.Models.Identity;
using MyFinancesAPI.Services.Abstractions;
using MyFinancesAPI.Services.EmailServices.Abstractions;
using System.Web;
using SignInResult = Microsoft.AspNetCore.Identity.SignInResult;

namespace MyFinancesAPI.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class AuthenticationController(
        IJwtProvider jetProvider,
        UserManager<User> userManager,
        SignInManager<User> signInManager,
        IEmailService emailService) : ControllerBase
    {
        private readonly IJwtProvider jwtProvider = jetProvider;
        private readonly SignInManager<User> signInManager = signInManager;
        private readonly IEmailService emailService = emailService;
        private readonly UserManager<User> userManager = userManager;
        private static DateTimeOffset DefaultExpireTime => DateTimeOffset.UtcNow.AddMinutes(3);

        [HttpPost("login")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Login([FromBody] LoginDto credential)
        {
            User? user = await userManager.FindByEmailAsync(credential.Email!);
            if (user is null)
            {
                return Unauthorized("Invalid credentials");
            }

            SignInResult results = await signInManager.CheckPasswordSignInAsync(user, credential.Password!, lockoutOnFailure: false);
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
            var user = new User
            {
                FirstName = registerDto.Name!,
                UserName = registerDto.Email,
                Email = registerDto.Email,
            };

            IdentityResult result = await userManager.CreateAsync(user, registerDto.Password!);
            if (result.Succeeded)
            {
                return Ok();
            }
            string[] errorDescriptions = [.. result.Errors.Select(error => error.Description)];
            return BadRequest(new { Errors = errorDescriptions });
        }

        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto forgotPasswordDto)
        {
            User? user = await userManager.FindByEmailAsync(forgotPasswordDto.Email);
            if (user is null)
            {
                return Ok(); // Do not reveal whether the user exists for security reasons
            }

            try
            {
                string token = await userManager.GeneratePasswordResetTokenAsync(user);
                string encodedToken = HttpUtility.UrlEncode(token);
                string encodedUserId = HttpUtility.UrlEncode(user.Id);
                await emailService.SendForgotPasswordAsync(forgotPasswordDto.Email, encodedToken, encodedUserId, forgotPasswordDto.FrontedBaseUrl);
                return Ok();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending forgot password email: {ex.Message}");
                return StatusCode(StatusCodes.Status500InternalServerError, "Failed to send email");
            }
        }

        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto resetPasswordDto)
        {
            string decodedUserId = HttpUtility.UrlDecode(resetPasswordDto.UserId);
            User? user = await userManager.FindByIdAsync(decodedUserId);
            if (user is null)
            {
                return BadRequest("Invalid email address");
            }

            string decodedToken = HttpUtility.UrlDecode(resetPasswordDto.Token);
            IdentityResult result = await userManager.ResetPasswordAsync(user, decodedToken, resetPasswordDto.NewPassword!);
            if (result.Succeeded)
            {
                return Ok();
            }
            string[] errorDescriptions = [.. result.Errors.Select(error => error.Description)];
            return BadRequest(new { Errors = errorDescriptions });
        }
    }
}