using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MyFinancesAPI.Models.Configurations;
using MyFinancesAPI.Models.Identity;
using MyFinancesAPI.Services.Auth;
using System.Net.Mail;
using SignInResult = Microsoft.AspNetCore.Identity.SignInResult;

namespace MyFinancesAPI.Controllers.Authentication
{
    [ApiController]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public partial class AuthController(
        IOptions<FrontendSettings> options,
        AuthService authService,
        ILogger<AuthController> logger) : ControllerBase
    {
        private readonly AuthService authService = authService;
        private readonly ILogger<AuthController> logger = logger;
        private readonly FrontendSettings options = options.Value;

        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPasswordAsync([FromBody] ForgotPasswordDto dto)
        {
            try
            {
                await authService.ForgotPasswordAsync(dto);
                return Ok();
            }
            catch (SmtpException ex)
            {
                logger.LogError(ex, "SMTP failed for {Email}", dto.Email);
                return StatusCode(500, "Could not send email");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error in ForgotPassword");
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpPost("login")]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> Login([FromBody] LoginDto dto)
        {
            try
            {
                var result = await authService.LoginAsync(dto);
                if (!result.Succeeded)
                {
                    return result.Errors.Any(error =>
                    error.Code == "UserNotFound" ||
                    error.Code == "InvalidCredentials" ||
                    error.Code == "EmailNotConfirmed") ?
                    Unauthorized(new { Errors = result.Errors.Select(error => error.Description).ToArray() }) :
                    BadRequest(new { Errors = result.Errors.Select(error => error.Description).ToArray() });
                }
                return Ok(new { result.Token, result.ExpireAt });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error in Login");
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterDto registerDto)
        {
            User user = mapper.Map<User>(registerDto);
            IdentityResult result = await userManager.CreateAsync(user, registerDto.Password!);
            if (result.Succeeded)
            {
                try
                {
                    if (HasExternalProvider(registerDto))
                    {
                        bool success = await AddExternalProvider(registerDto, user);
                        if (!success)
                        {
                            return BadRequest("Failed to add external provider");
                        }
                    }
                    SendValidationEmailDto validationEmailDto = mapper.Map<SendValidationEmailDto>(registerDto);
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