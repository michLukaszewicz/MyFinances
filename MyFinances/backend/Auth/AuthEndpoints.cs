using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;

namespace MyFinances.Api.Auth;

public static class AuthEndpoints
{
    // Nested under the /api group so /api/auth/logout and /api/auth/me inherit
    // RequireAuthorization(); register/login/antiforgery-token opt back out with
    // AllowAnonymous() since there is no session yet when they're called.
    public static void MapAuthEndpoints(this IEndpointRouteBuilder api)
    {
        var auth = api.MapGroup("/auth");

        auth.MapPost("/register", async (RegisterRequest request, UserManager<AppUser> userManager, SignInManager<AppUser> signInManager, IConfiguration configuration) =>
        {
            var allowedEmail = configuration["Auth:AllowedEmail"];
            if (string.IsNullOrEmpty(allowedEmail) || !string.Equals(request.Email, allowedEmail, StringComparison.OrdinalIgnoreCase))
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Registration is not allowed for this email address.");
            }

            var user = new AppUser
            {
                UserName = request.Email,
                Email = request.Email
            };

            var result = await userManager.CreateAsync(user, request.Password);
            if (!result.Succeeded)
            {
                var errors = result.Errors.ToDictionary(e => e.Code, e => new[] { e.Description });
                return Results.ValidationProblem(errors);
            }

            await signInManager.SignInAsync(user, isPersistent: true);
            return Results.Ok(new { email = user.Email });
        })
        .AllowAnonymous();

        auth.MapPost("/login", async (LoginRequest request, SignInManager<AppUser> signInManager) =>
        {
            var result = await signInManager.PasswordSignInAsync(request.Email, request.Password, isPersistent: true, lockoutOnFailure: false);
            if (!result.Succeeded)
            {
                return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid credentials.");
            }

            return Results.Ok(new { email = request.Email });
        })
        .AllowAnonymous();

        auth.MapPost("/logout", async (SignInManager<AppUser> signInManager) =>
        {
            await signInManager.SignOutAsync();
            return Results.Ok();
        })
        .AddEndpointFilter(async (context, next) =>
        {
            var antiforgery = context.HttpContext.RequestServices.GetRequiredService<IAntiforgery>();
            try
            {
                await antiforgery.ValidateRequestAsync(context.HttpContext);
            }
            catch (AntiforgeryValidationException)
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid antiforgery token");
            }

            return await next(context);
        });

        auth.MapGet("/me", (ClaimsPrincipal user) =>
        {
            var email = user.FindFirstValue(ClaimTypes.Email);
            return Results.Ok(new { email });
        });

        auth.MapGet("/antiforgery-token", (IAntiforgery antiforgery, HttpContext httpContext) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(httpContext);
            return Results.Ok(new { token = tokens.RequestToken });
        })
        .AllowAnonymous();
    }
}

public record RegisterRequest(string Email, string Password);
public record LoginRequest(string Email, string Password);
