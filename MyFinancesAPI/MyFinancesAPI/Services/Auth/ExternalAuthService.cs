using Microsoft.AspNetCore.Identity;
using MyFinancesAPI.Models.Identity;
using MyFinancesAPI.Models.OperationResults;

namespace MyFinancesAPI.Services.Auth
{
    public class ExternalAuthService(UserManager<User> userManager)
    {
        private readonly UserManager<User> userManager = userManager;

        public async Task<OperationResult> AddExternalLoginProvider(LoginDto dto, User user)
        {
            if (await HasUserProviderAssigned(dto, user))
            {
                var userLoginInfo = new UserLoginInfo(dto.ProviderName!, dto.ProviderKey!, dto.ProviderName);
                IdentityResult addLoginResult = await userManager.AddLoginAsync(user, userLoginInfo);
                if (!addLoginResult.Succeeded)
                {
                    return OperationResult.Failed(new IdentityError
                    {
                        Code = "LinkExternalProviderFailed",
                        Description = "Failed to link external provider"
                    });
                }
            }
            return OperationResult.Success();
        }

        private async Task<bool> HasUserProviderAssigned(LoginDto credential, User user)
        {
            IList<UserLoginInfo> existingLogins = await userManager.GetLoginsAsync(user);
            return existingLogins.Any(l => l.LoginProvider == credential.ProviderName && l.ProviderKey == credential.ProviderKey);
        }
    }
}