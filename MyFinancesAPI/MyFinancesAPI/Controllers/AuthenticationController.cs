using AutoMapper;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using MyFinancesAPI.Models.Identity;
using MyFinancesAPI.Services.Abstractions;
using MyFinancesAPI.Services.EmailServices.Abstractions;
using SignInResult = Microsoft.AspNetCore.Identity.SignInResult;

namespace MyFinancesAPI.Controllers
{
    [Route("[controller]")]
    [ApiController]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public class AuthenticationController(
        IJwtProvider jetProvider,
        UserManager<User> userManager,
        SignInManager<User> signInManager,
        IEmailService emailService,
        IMapper mapper) : ControllerBase
    {
        private readonly IEmailService emailService = emailService;
        private readonly IJwtProvider jwtProvider = jetProvider;
        private readonly IMapper mapper = mapper;
        private readonly SignInManager<User> signInManager = signInManager;
        private readonly UserManager<User> userManager = userManager;
        private static DateTimeOffset DefaultExpireTime => DateTimeOffset.UtcNow.AddMinutes(3);

        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto forgotPasswordDto)
        {
            User? user = await userManager.FindByEmailAsync(forgotPasswordDto.Email);
            if (user is null)
            {
                return Ok();
            }

            try
            {
                string token = await userManager.GeneratePasswordResetTokenAsync(user);
                await emailService.SendForgotPasswordAsync(forgotPasswordDto.Email, token, user.Id, forgotPasswordDto.FrontedBaseUrl);
                return Ok();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending forgot password email: {ex.Message}");
                return StatusCode(StatusCodes.Status500InternalServerError, "Failed to send email");
            }
        }

        [HttpPost("login")]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> Login([FromBody] LoginDto credential)
        {
            User? user = await userManager.FindByEmailAsync(credential.Email!);
            if (user is null)
            {
                return Unauthorized("Invalid credentials");
            }
            else if (!user.EmailConfirmed)
            {
                return Unauthorized("Email not confirmed");
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
            return Unauthorized("Invalid credentials");
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterDto registerDto)
        {
            var user = mapper.Map<User>(registerDto);
            IdentityResult result = await userManager.CreateAsync(user, registerDto.Password!);
            if (result.Succeeded)
            {
                try
                {
                    var validationEmailDto = mapper.Map<SendValidationEmailDto>(registerDto);
                    await SendValidationEmailAsync(validationEmailDto);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error sending confirmation email: {ex.Message}");
                    return StatusCode(StatusCodes.Status500InternalServerError, "Failed to send confirmation email");
                }
                return Ok();
            }
            string[] errorDescriptions = [.. result.Errors.Select(error => error.Description)];
            return BadRequest(new { Errors = errorDescriptions });
        }

        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto resetPasswordDto)
        {
            User? user = await userManager.FindByIdAsync(resetPasswordDto.UserId);
            if (user is null)
            {
                return Ok();
            }

            IdentityResult result = await userManager.ResetPasswordAsync(user, resetPasswordDto.Token, resetPasswordDto.NewPassword!);
            if (result.Succeeded)
            {
                return Ok();
            }
            string[] errorDescriptions = [.. result.Errors.Select(error => error.Description)];
            return BadRequest(new { Errors = errorDescriptions });
        }

        [HttpPost("send-validation-email")]
        public async Task<IActionResult> SendValidationEmailAsync([FromBody] SendValidationEmailDto sendValidationEmailDto)
        {
            try
            {
                User? user = await userManager.FindByEmailAsync(sendValidationEmailDto.Email!);
                if (user is null || user.EmailConfirmed)
                {
                    return Ok();
                }
                string token = await userManager.GenerateEmailConfirmationTokenAsync(user);
                await emailService.SendValidateEmailAsync(user.Email!, token, user.Id, sendValidationEmailDto.FrontendBaseUrl!);
                return Ok();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending validation email: {ex.Message}");
                return StatusCode(StatusCodes.Status500InternalServerError, "Failed to send validation email");
            }
        }

        [HttpPost("validate-email")]
        public async Task<IActionResult> ValidateEmail([FromBody] ValidateEmailDto validateEmailDto)
        {
            User? user = await userManager.FindByIdAsync(validateEmailDto.UserId!);
            if (user is null)
            {
                return Ok();
            }
            IdentityResult result = await userManager.ConfirmEmailAsync(user, validateEmailDto.Token!);
            if (result.Succeeded)
            {
                return Ok();
            }
            string[] errorDescriptions = [.. result.Errors.Select(error => error.Description)];
            return BadRequest(new { Errors = errorDescriptions });
        }
    }
}