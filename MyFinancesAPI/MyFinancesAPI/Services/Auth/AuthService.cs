using AutoMapper;
using Microsoft.AspNetCore.Identity;
using MyFinancesAPI.Models.Identity;
using MyFinancesAPI.Models.OperationResults;
using MyFinancesAPI.Services.Abstractions;
using MyFinancesAPI.Services.EmailServices.Abstractions;

namespace MyFinancesAPI.Services.Auth
{
    public class AuthService(
        IJwtProvider jwtProvider,
        UserManager<User> userManager,
        SignInManager<User> signInManager,
        IEmailService emailService,
        ExternalAuthService externalAuthService)
    {
        private readonly IEmailService emailService = emailService;
        private readonly ExternalAuthService externalAuthService = externalAuthService;
        private readonly IJwtProvider jwtProvider = jwtProvider;
        private readonly SignInManager<User> signInManager = signInManager;
        private readonly UserManager<User> userManager = userManager;
        private static DateTimeOffset DefaultExpireTime => DateTimeOffset.UtcNow.AddMinutes(3);

        internal async Task ForgotPasswordAsync(ForgotPasswordDto forgotPasswordDto)
        {
            User? user = await userManager.FindByEmailAsync(forgotPasswordDto.Email);
            if (user is null)
            {
                return;
            }

            string token = await userManager.GeneratePasswordResetTokenAsync(user);
            await emailService.SendForgotPasswordAsync(forgotPasswordDto.Email, token, user.Id, forgotPasswordDto.FrontedBaseUrl);
        }

        internal async Task<OperationResult> LoginAsync(LoginDto dto)
        {
            User? user = await userManager.FindByEmailAsync(dto.Email!);
            if (user is null)
            {
                return OperationResult.Failed(new IdentityError
                {
                    Code = "UserNotFound",
                    Description = "User with this email does not exist"
                });
            }
            else if (!user.EmailConfirmed)
            {
                return OperationResult.Failed(new IdentityError
                {
                    Code = "EmailNotConfirmed",
                    Description = "Email not confirmed"
                });
            }
            var signInResults = await signInManager.CheckPasswordSignInAsync(user, dto.Password!, lockoutOnFailure: false);
            if (signInResults.Succeeded)
            {
                string token = jwtProvider.GetJwtTokenForUser(DefaultExpireTime, user);
                OperationResult result = OperationResult.Success(token, DefaultExpireTime);
                if (HasExternalProviderInfo(dto))
                {
                    result.Errors = (await externalAuthService.AddExternalLoginProvider(dto, user)).Errors;
                }
                return result;
            }
            return OperationResult.Failed(new IdentityError
            {
                Code = "Invalid credentials",
                Description = "Invalid credentials"
            });
        }

        private static bool HasExternalProviderInfo(IExternalProvider dto) =>
            !string.IsNullOrEmpty(dto.ProviderName) && !string.IsNullOrEmpty(dto.ProviderKey);
    }
}