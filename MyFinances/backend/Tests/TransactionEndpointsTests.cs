using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyFinances.Api;
using MyFinances.Api.Auth;
using MyFinances.Api.Transactions;
using Xunit;

namespace MyFinances.Api.Tests;

// Coverage for GET /api/transactions: auth, per-user scoping, ordering, pagination
// boundaries, and the empty result set. Follows ImportEndpointsTests.cs's conventions
// (AuthApiFactory + TestClientHelpers, MethodUnderTest_Scenario_ExpectedResult naming).
public class TransactionEndpointsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static async Task<AccountDto> CreateAccountAsync(HttpClient client, string bankName, string accountNumber)
    {
        var response = await client.GetAsync("/api/auth/antiforgery-token");
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<TokenResponse>(JsonOptions);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/accounts/")
        {
            Content = JsonContent.Create(new AccountWriteRequest(bankName, accountNumber)),
        };
        request.Headers.Add("X-XSRF-TOKEN", payload!.Token);
        var createResponse = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var dto = await createResponse.Content.ReadFromJsonAsync<AccountDto>(JsonOptions);
        return dto!;
    }

    private record TokenResponse(string Token);

    // Seeds `count` transactions directly via AppDbContext, dated so that a higher index is
    // newer (baseDate + index days) — matches the endpoint's newest-first ordering contract.
    private static async Task SeedTransactionsAsync(AuthApiFactory factory, Guid userId, Guid accountId, int count, DateOnly baseDate)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        for (var i = 0; i < count; i++)
        {
            db.Transactions.Add(new Transaction
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                AccountId = accountId,
                Date = baseDate.AddDays(i),
                Description = $"Transaction {i}",
                Amount = 10m + i,
                Hash = $"hash-{userId}-{i}",
            });
        }

        await db.SaveChangesAsync();
    }

    private static async Task<Guid> GetUserIdAsync(AuthApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = await userManager.FindByEmailAsync(AuthApiFactory.AllowedEmail);
        return user!.Id;
    }

    [Fact]
    public async Task List_WithoutAuthCookie_ReturnsUnauthorized()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/transactions");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task List_UserHasNoTransactions_ReturnsEmptyItemsAndHasMoreFalse()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);

        var response = await client.GetAsync("/api/transactions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<TransactionListResponseDto>(JsonOptions);
        Assert.NotNull(result);
        Assert.Empty(result!.Items);
        Assert.False(result.HasMore);
    }

    [Fact]
    public async Task List_OnlyReturnsCurrentUsersTransactions()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var account = await CreateAccountAsync(client, "mBank", "111");
        var userId = await GetUserIdAsync(factory);
        await SeedTransactionsAsync(factory, userId, account.Id, 3, new DateOnly(2026, 1, 1));

        // A second, unrelated user with their own account and transactions.
        var otherUserId = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var otherAccount = new Account { Id = Guid.NewGuid(), UserId = otherUserId, BankName = "mBank", AccountNumber = "222" };
            db.Accounts.Add(otherAccount);
            await db.SaveChangesAsync();
            await SeedTransactionsAsync(factory, otherUserId, otherAccount.Id, 5, new DateOnly(2026, 1, 1));
        }

        var response = await client.GetAsync("/api/transactions?skip=0&take=100");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<TransactionListResponseDto>(JsonOptions);
        Assert.NotNull(result);
        Assert.Equal(3, result!.Items.Count);
    }

    [Fact]
    public async Task List_ReturnsTransactionsNewestFirst()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var account = await CreateAccountAsync(client, "mBank", "111");
        var userId = await GetUserIdAsync(factory);
        await SeedTransactionsAsync(factory, userId, account.Id, 5, new DateOnly(2026, 1, 1));

        var response = await client.GetAsync("/api/transactions?skip=0&take=100");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<TransactionListResponseDto>(JsonOptions);
        Assert.NotNull(result);
        var dates = result!.Items.Select(i => i.Date).ToList();
        Assert.Equal(dates.OrderByDescending(d => d), dates);
        Assert.Equal(new DateOnly(2026, 1, 5), dates.First());
    }

    [Fact]
    public async Task List_SkipAndTake_PaginateCorrectly()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var account = await CreateAccountAsync(client, "mBank", "111");
        var userId = await GetUserIdAsync(factory);
        await SeedTransactionsAsync(factory, userId, account.Id, 25, new DateOnly(2026, 1, 1));

        var firstPageResponse = await client.GetAsync("/api/transactions?skip=0&take=20");
        Assert.Equal(HttpStatusCode.OK, firstPageResponse.StatusCode);
        var firstPage = await firstPageResponse.Content.ReadFromJsonAsync<TransactionListResponseDto>(JsonOptions);
        Assert.NotNull(firstPage);
        Assert.Equal(20, firstPage!.Items.Count);
        Assert.True(firstPage.HasMore);

        var secondPageResponse = await client.GetAsync("/api/transactions?skip=20&take=20");
        Assert.Equal(HttpStatusCode.OK, secondPageResponse.StatusCode);
        var secondPage = await secondPageResponse.Content.ReadFromJsonAsync<TransactionListResponseDto>(JsonOptions);
        Assert.NotNull(secondPage);
        Assert.Equal(5, secondPage!.Items.Count);
        Assert.False(secondPage.HasMore);

        // No overlap between pages.
        var firstIds = firstPage.Items.Select(i => i.Id).ToHashSet();
        Assert.DoesNotContain(secondPage.Items, i => firstIds.Contains(i.Id));
    }

    [Fact]
    public async Task List_LastPage_HasMoreIsFalse()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var account = await CreateAccountAsync(client, "mBank", "111");
        var userId = await GetUserIdAsync(factory);
        await SeedTransactionsAsync(factory, userId, account.Id, 10, new DateOnly(2026, 1, 1));

        var response = await client.GetAsync("/api/transactions?skip=0&take=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<TransactionListResponseDto>(JsonOptions);
        Assert.NotNull(result);
        Assert.Equal(10, result!.Items.Count);
        Assert.False(result.HasMore);
    }
}
