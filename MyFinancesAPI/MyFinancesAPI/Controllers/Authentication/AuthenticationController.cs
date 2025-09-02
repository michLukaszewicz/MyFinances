using AutoMapper;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MyFinancesAPI.Models.Configurations;
using MyFinancesAPI.Models.Identity;
using MyFinancesAPI.Services.Abstractions;
using MyFinancesAPI.Services.EmailServices.Abstractions;
using SignInResult = Microsoft.AspNetCore.Identity.SignInResult;

namespace MyFinancesAPI.Controllers.Authentication
{
    [Route("auth")]
    [ApiController]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public partial class AuthenticationController(
        IJwtProvider jwtProvider,
        UserManager<User> userManager,
        SignInManager<User> signInManager,
        IEmailService emailService,
        IMapper mapper,
        IOptions<FrontendSettings> options) : ControllerBase
    {
        private readonly IEmailService emailService = emailService;
        private readonly IJwtProvider jwtProvider = jwtProvider;
        private readonly IMapper mapper = mapper;
        private readonly FrontendSettings options = options.Value;
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
                if (HasExternalProvider(credential) && await HasUserProviderAssigned(credential, user))
                {
                    var userLoginInfo = new UserLoginInfo(credential.ProviderName!, credential.ProviderKey!, credential.ProviderName);
                    IdentityResult addLoginResult = await userManager.AddLoginAsync(user, userLoginInfo);
                    if (!addLoginResult.Succeeded)
                    {
                        return BadRequest("Failed to link external provider");
                    }
                }
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

        private static bool HasExternalProvider(IExternalProvider registerDto)
        {
            return !string.IsNullOrEmpty(registerDto.ProviderName) && !string.IsNullOrEmpty(registerDto.ProviderKey);
        }

        private async Task<bool> AddExternalProvider(RegisterDto registerDto, User user)
        {
            var loginInfo = new UserLoginInfo(registerDto.ProviderName!, registerDto.ProviderKey!, registerDto.ProviderName);
            IdentityResult loginResults = await userManager.AddLoginAsync(user, loginInfo);
            return loginResults.Succeeded;
        }

        private async Task<bool> HasUserProviderAssigned(LoginDto credential, User user)
        {
            IList<UserLoginInfo> existingLogins = await userManager.GetLoginsAsync(user);
            return existingLogins.Any(l => l.LoginProvider == credential.ProviderName && l.ProviderKey == credential.ProviderKey);
        }
    }
}