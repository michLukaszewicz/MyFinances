using AutoMapper;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MyFinancesAPI.Models.Configurations;
using MyFinancesAPI.Models.Identity;
using MyFinancesAPI.Services.Abstractions;
using MyFinancesAPI.Services.EmailServices.Abstractions;
using PasswordGenerator;
using System.Security.Claims;
using SignInResult = Microsoft.AspNetCore.Identity.SignInResult;

namespace MyFinancesAPI.Controllers
{
    [Route("[controller]")]
    [ApiController]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public class AuthenticationController(
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

        [HttpGet("google-login")]
        public IActionResult GoogleLogin(string redirectUrl = "")
        {
            string? redirectUri = Url.Action(nameof(GoogleResponse), "Authentication", new { redirectUrl }, Request.Scheme);
            Microsoft.AspNetCore.Authentication.AuthenticationProperties properties = signInManager.ConfigureExternalAuthenticationProperties(GoogleDefaults.AuthenticationScheme, redirectUri);
            return Challenge(properties, GoogleDefaults.AuthenticationScheme);
        }

        //TODO: Move google-methods to a separate controller.
        //TODO: Add frontendURL in frontend to be able to send valid token in case where we register the new user
        [HttpGet("google-response")]
        public async Task<IActionResult> GoogleResponse(string redirectUrl = "/")
        {
            ExternalLoginInfo? info = await signInManager.GetExternalLoginInfoAsync();
            if (info is null)
            {
                return Redirect($"{options.BaseUrl}/login?error=external-login-info-not-found");
            }

            string? email = info.Principal.FindFirstValue(ClaimTypes.Email);
            if (email is null)
            {
                return Redirect($"{options.BaseUrl}/login?error=email-not-found");
            }

            SignInResult result = await signInManager.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, isPersistent: false, bypassTwoFactor: true);
            if (result.Succeeded)
            {
                User? user = await userManager.FindByEmailAsync(email);
                if (user is null)
                {
                    return Redirect($"{options.BaseUrl}/login?error=external-login-info-not-found");
                }

                string token = jwtProvider.GetJwtTokenForUser(DefaultExpireTime, user);
                return Redirect($"{options.BaseUrl}/{redirectUrl}?access_token={token}&expires_at={DefaultExpireTime}");
            }

            User? existingUser = await userManager.FindByEmailAsync(email);
            if (existingUser is not null)
            {
                IList<UserLoginInfo> loginProviders = await userManager.GetLoginsAsync(existingUser);
                bool hasGoogleLogin = loginProviders.Any(login => login.LoginProvider == info.LoginProvider && login.ProviderKey == info.ProviderKey);
                if (hasGoogleLogin)
                {
                    return Redirect($"{options.BaseUrl}/login?error=external-login-not-linked");
                }

                return Redirect($"{options.BaseUrl}/login?error=external-login-failed");
            }

            string name = info.Principal.FindFirstValue(ClaimTypes.Name) ?? "User";
            string password = new Password().IncludeNumeric().IncludeSpecial().IncludeLowercase().IncludeUppercase().LengthRequired(12).Next();
            var registerDto = new RegisterDto()
            {
                Name = name,
                Email = email,
                Password = password,
                ConfirmPassword = password,
            };

            IActionResult registerResult = await Register(registerDto);
            if (registerResult is OkResult || registerResult is ObjectResult { StatusCode: 200 })
            {
                User? newUser = await userManager.FindByEmailAsync(email);
                if (newUser is null)
                {
                    return Redirect($"{options.BaseUrl}/login?error=external-login-failed");
                }
                IdentityResult linkResult = await userManager.AddLoginAsync(newUser, info);
                if (!linkResult.Succeeded)
                {
                    string[] errorDescriptions = [.. linkResult.Errors.Select(error => error.Description)];
                    return BadRequest(new { Errors = errorDescriptions });
                }
                string token = jwtProvider.GetJwtTokenForUser(DefaultExpireTime, newUser);
                return Redirect($"{options.BaseUrl}/{redirectUrl}?access_token={token}&expires_at={DefaultExpireTime}");
            }

            return Redirect($"{options.BaseUrl}/login?error=external-login-failed");
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
            User user = mapper.Map<User>(registerDto);
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