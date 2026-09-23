using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyFinances.Api;
using MyFinances.Api.Auth;
using Xunit;

namespace MyFinances.Api.Tests;

/// <summary>
/// Custom factory: swaps the real Npgsql-backed AppDbContext for a uniquely-named
/// EF Core InMemory database per instance, and supplies Auth:AllowedEmail (the checked-in
/// appsettings files intentionally ship it empty). Keeps tests hermetic and fast, with no
/// dependency on the real Neon dev database.
/// </summary>
public class AuthApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    public const string AllowedEmail = "test@example.com";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:AllowedEmail"] = AllowedEmail
            });
        });

        builder.ConfigureServices(services =>
        {
            // Program.cs registers AppDbContext with UseNpgsql, which — via AddDbContext's
            // "external service provider" mode — adds provider-marker services (e.g.
            // IDatabaseProvider) straight into the app's IServiceCollection, not just onto
            // DbContextOptions<AppDbContext>. Removing only the DbContextOptions descriptor
            // leaves those marker services behind, and EF Core then throws "multiple database
            // providers registered" once UseInMemoryDatabase adds its own marker alongside them.
            // Strip every EF Core Npgsql/DbContextOptions registration before re-adding InMemory.
            var descriptorsToRemove = services
                .Where(d =>
                    d.ServiceType == typeof(DbContextOptions<AppDbContext>) ||
                    d.ServiceType == typeof(DbContextOptions) ||
                    (d.ServiceType.FullName?.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) ?? false) ||
                    (d.ServiceType.FullName?.Contains("Npgsql", StringComparison.Ordinal) ?? false) ||
                    (d.ImplementationType?.FullName?.Contains("Npgsql", StringComparison.Ordinal) ?? false))
                .ToList();
            foreach (var descriptor in descriptorsToRemove)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(_dbName));
        });
    }
}

public class AuthEndpointsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static bool HasSetCookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values) && values.Any();

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/auth/antiforgery-token");
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<TokenResponse>(JsonOptions);
        return payload!.Token;
    }

    private record TokenResponse(string Token);

    [Fact]
    public async Task Register_WithAllowedEmail_SucceedsAndSetsCookie()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(AuthApiFactory.AllowedEmail, "correct-horse-battery"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(HasSetCookie(response));
    }

    [Fact]
    public async Task Register_WithNonAllowedEmail_ReturnsBadRequest()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest("someone-else@example.com", "correct-horse-battery"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_WithShortPassword_ReturnsBadRequest()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(AuthApiFactory.AllowedEmail, "short1"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_Twice_SecondAttemptFails()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();

        var first = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(AuthApiFactory.AllowedEmail, "correct-horse-battery"));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(AuthApiFactory.AllowedEmail, "correct-horse-battery"));
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [Fact]
    public async Task Login_WithCorrectCredentials_SucceedsAndSetsCookie_WithWrongPassword_Returns401()
    {
        using var factory = new AuthApiFactory();
        using var registerClient = factory.CreateClient();
        var registerResponse = await registerClient.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(AuthApiFactory.AllowedEmail, "correct-horse-battery"));
        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);

        using var loginClient = factory.CreateClient();
        var loginResponse = await loginClient.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(AuthApiFactory.AllowedEmail, "correct-horse-battery"));
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        Assert.True(HasSetCookie(loginResponse));

        using var wrongPasswordClient = factory.CreateClient();
        var wrongPasswordResponse = await wrongPasswordClient.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(AuthApiFactory.AllowedEmail, "totally-wrong-password"));
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPasswordResponse.StatusCode);
    }

    [Fact]
    public async Task AuthMe_RequiresAuthCookie()
    {
        using var factory = new AuthApiFactory();

        using var anonymousClient = factory.CreateClient();
        var unauthorized = await anonymousClient.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        using var authedClient = factory.CreateClient();
        var registerResponse = await authedClient.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(AuthApiFactory.AllowedEmail, "correct-horse-battery"));
        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);

        var authorized = await authedClient.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, authorized.StatusCode);
    }

    [Fact]
    public async Task Logout_RequiresAntiforgeryHeader_ThenInvalidatesSession()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();

        var registerResponse = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(AuthApiFactory.AllowedEmail, "correct-horse-battery"));
        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);

        // Without the antiforgery header, logout must be rejected.
        var logoutWithoutToken = await client.PostAsync("/api/auth/logout", content: null);
        Assert.Equal(HttpStatusCode.BadRequest, logoutWithoutToken.StatusCode);

        // The session should still be usable, since the logout above did not succeed.
        var stillAuthorized = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, stillAuthorized.StatusCode);

        var antiforgeryToken = await GetAntiforgeryTokenAsync(client);
        using var logoutRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        logoutRequest.Headers.Add("X-XSRF-TOKEN", antiforgeryToken);
        var logoutWithToken = await client.SendAsync(logoutRequest);
        Assert.Equal(HttpStatusCode.OK, logoutWithToken.StatusCode);

        // The now-stale session cookie must no longer grant access.
        var afterLogout = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, afterLogout.StatusCode);
    }
}
