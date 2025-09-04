using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using MyFinancesAPI.Models.Identity;
using System.Security.Claims;
using SignInResult = Microsoft.AspNetCore.Identity.SignInResult;

namespace MyFinancesAPI.Controllers.Authentication
{
    [Route("auth")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public partial class AuthController
    {
        [HttpGet("google-login")]
        public IActionResult GoogleLogin(string redirectUrl = "")
        {
            string? redirectUri = Url.Action(nameof(GoogleResponse), "Authentication", new { redirectUrl }, Request.Scheme);
            Microsoft.AspNetCore.Authentication.AuthenticationProperties properties = signInManager.ConfigureExternalAuthenticationProperties(GoogleDefaults.AuthenticationScheme, redirectUri);
            return Challenge(properties, GoogleDefaults.AuthenticationScheme);
        }

        [HttpGet("google-response")]
        public async Task<IActionResult> GoogleResponse(string redirectUrl = "/")
        {
            //GetInfo for user
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

            //Try to log in (if user has external login added)
            SignInResult result = await signInManager.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, isPersistent: false, bypassTwoFactor: true);
            if (result.Succeeded)
            {
                User? user = await userManager.FindByEmailAsync(email);
                if (user is null)
                {
                    return Redirect($"{options.BaseUrl}/login?error=external-login-info-not-found");
                }

                string token = jwtProvider.GetJwtTokenForUser(DefaultExpireTime, user);
                return Redirect($"{options.BaseUrl}{redirectUrl}?access_token={token}&expires_at={DefaultExpireTime}");
            }

            //Try to find user by email (if it exists in database but has no external login assigned)
            User? existingUser = await userManager.FindByEmailAsync(email);
            if (existingUser is not null)
            {
                IList<UserLoginInfo> loginProviders = await userManager.GetLoginsAsync(existingUser);
                bool hasGoogleLogin = loginProviders.Any(login => login.LoginProvider == info.LoginProvider && login.ProviderKey == info.ProviderKey);
                if (hasGoogleLogin)
                {
                    return Redirect($"{options.BaseUrl}/login?error=external-login-failed");
                }
                return Redirect($"{options.BaseUrl}/external-login?provider={info.ProviderDisplayName}&providerKey={info.ProviderKey}");
            }

            //User has no account in the database, redirect to dedicated site
            string name = info.Principal.FindFirstValue(ClaimTypes.Name) ?? string.Empty;
            string encodedName = Uri.EscapeDataString(name);
            string encodedEmail = Uri.EscapeDataString(email);
            return Redirect($"{options.BaseUrl}complete-registration?email={encodedEmail}&name={encodedName}&provider=Google&providerKey={info.ProviderKey}");
        }
    }
}