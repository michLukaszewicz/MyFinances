using System.Net;
using System.Net.Http.Json;
using MyFinances.Api.Auth;
using Xunit;

namespace MyFinances.Api.Tests;

// Shared helper for tests that need an authenticated HttpClient against an AuthApiFactory.
// Extracted from ImportEndpointsTests so TransactionEndpointsTests can reuse it instead of
// duplicating the register-and-return-client boilerplate.
internal static class TestClientHelpers
{
    public static async Task<HttpClient> CreateAuthenticatedClientAsync(AuthApiFactory factory)
    {
        var client = factory.CreateClient();
        var registerResponse = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(AuthApiFactory.AllowedEmail, "correct-horse-battery"));
        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);
        return client;
    }
}
