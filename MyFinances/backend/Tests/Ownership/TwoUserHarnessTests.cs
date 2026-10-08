using MyFinances.Api.Tests.Support;
using Xunit;

namespace MyFinances.Api.Tests.Ownership;

public class TwoUserHarnessTests : IAsyncLifetime
{
    private TwoUserHarness harness = null!;

    public async Task InitializeAsync() => harness = await TwoUserHarness.CreateAsync();

    public Task DisposeAsync()
    {
        harness.Dispose();
        return Task.CompletedTask;
    }

    private record MeResponse(string Email);

    [Fact]
    public async Task Me_ReturnsDifferentEmailsForTheTwoSessions()
    {
        // Act
        var meA = await TwoUserHarness.GetJsonAsync<MeResponse>(harness.ClientA, "/api/auth/me");
        var meB = await TwoUserHarness.GetJsonAsync<MeResponse>(harness.ClientB, "/api/auth/me");
        // Assert
        Assert.Equal("test@example.com", meA.Email);
        Assert.Equal("other@example.com", meB.Email);
        Assert.NotEqual(harness.UserIdA, harness.UserIdB);
    }
}