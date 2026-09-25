using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyFinances.Api.Auth;
using MyFinances.Api.Transactions;
using Xunit;

namespace MyFinances.Api.Tests;

// Phase 3 coverage for AccountEndpoints: reuses AuthApiFactory's WebApplicationFactory +
// EF Core InMemory swap (same pattern as ImportEndpointsTests).
public class AccountEndpointsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // The app only allows a single AllowedEmail to register (single-user MVP — see CLAUDE.md),
    // so "a different user" scenarios below insert a second Account row directly against a
    // synthetic UserId rather than trying to register a second account through the API.
    private static async Task<HttpClient> CreateAuthenticatedClientAsync(AuthApiFactory factory)
    {
        var client = factory.CreateClient();
        var registerResponse = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(AuthApiFactory.AllowedEmail, "correct-horse-battery"));
        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);
        return client;
    }

    private static async Task<Account> InsertAccountForOtherUserAsync(AuthApiFactory factory, string bank, string number)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var account = new Account { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), BankName = bank, AccountNumber = number };
        db.Accounts.Add(account);
        await db.SaveChangesAsync();
        return account;
    }

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/auth/antiforgery-token");
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<TokenResponse>(JsonOptions);
        return payload!.Token;
    }

    private record TokenResponse(string Token);

    private static async Task<HttpResponseMessage> PostAccountAsync(HttpClient client, string bank, string number)
    {
        var token = await GetAntiforgeryTokenAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/accounts/")
        {
            Content = JsonContent.Create(new AccountWriteRequest(bank, number)),
        };
        request.Headers.Add("X-XSRF-TOKEN", token);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PutAccountAsync(HttpClient client, Guid id, string bank, string number)
    {
        var token = await GetAntiforgeryTokenAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/accounts/{id}")
        {
            Content = JsonContent.Create(new AccountWriteRequest(bank, number)),
        };
        request.Headers.Add("X-XSRF-TOKEN", token);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> DeleteAccountAsync(HttpClient client, Guid id)
    {
        var token = await GetAntiforgeryTokenAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/accounts/{id}");
        request.Headers.Add("X-XSRF-TOKEN", token);
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task GetBanks_ReturnsRegisteredParserNamesPlusOther()
    {
        using var factory = new AuthApiFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);

        var response = await client.GetAsync("/api/accounts/banks");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<BankOptionsResponse>(JsonOptions);
        Assert.NotNull(payload);
        Assert.Equal(new[] { "mBank", "Other" }, payload!.BankNames);
    }

    [Fact]
    public async Task Create_WithNewBankAndNumber_Inserts()
    {
        using var factory = new AuthApiFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);

        var response = await PostAccountAsync(client, "mBank", "12345678901234567890123456");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<AccountDto>(JsonOptions);
        Assert.NotNull(dto);
        Assert.Equal("mBank", dto!.BankName);
    }

    [Fact]
    public async Task Create_WithDuplicateBankAndNumberForSameUser_ReturnsConflictWithoutInserting()
    {
        using var factory = new AuthApiFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);

        var first = await PostAccountAsync(client, "mBank", "111");
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await PostAccountAsync(client, "mBank", "111");
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        var list = await (await client.GetAsync("/api/accounts/")).Content.ReadFromJsonAsync<List<AccountDto>>(JsonOptions);
        Assert.Single(list!);
    }

    [Fact]
    public async Task Create_WithSameBankAndNumberForDifferentUser_Succeeds()
    {
        using var factory = new AuthApiFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);
        await InsertAccountForOtherUserAsync(factory, "mBank", "999");

        var response = await PostAccountAsync(client, "mBank", "999");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task List_ScopedToCurrentUserOnly()
    {
        using var factory = new AuthApiFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);

        await PostAccountAsync(client, "mBank", "111");
        await InsertAccountForOtherUserAsync(factory, "mBank", "222");

        var list = await (await client.GetAsync("/api/accounts/")).Content.ReadFromJsonAsync<List<AccountDto>>(JsonOptions);
        Assert.Single(list!);
        Assert.Equal("111", list![0].AccountNumber);
    }

    [Fact]
    public async Task Update_RecomputesFieldsAndExcludesSelfFromDuplicateCheck()
    {
        using var factory = new AuthApiFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);

        var created = await (await PostAccountAsync(client, "mBank", "111")).Content.ReadFromJsonAsync<AccountDto>(JsonOptions);

        var updateResponse = await PutAccountAsync(client, created!.Id, "mBank", "111");
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var updateResponse2 = await PutAccountAsync(client, created.Id, "Other", "222");
        Assert.Equal(HttpStatusCode.OK, updateResponse2.StatusCode);
        var updated = await updateResponse2.Content.ReadFromJsonAsync<AccountDto>(JsonOptions);
        Assert.Equal("Other", updated!.BankName);
        Assert.Equal("222", updated.AccountNumber);
    }

    [Fact]
    public async Task Update_CollidingWithDifferentAccount_ReturnsConflict()
    {
        using var factory = new AuthApiFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);

        var first = await (await PostAccountAsync(client, "mBank", "111")).Content.ReadFromJsonAsync<AccountDto>(JsonOptions);
        var second = await (await PostAccountAsync(client, "mBank", "222")).Content.ReadFromJsonAsync<AccountDto>(JsonOptions);

        var response = await PutAccountAsync(client, second!.Id, "mBank", "111");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Update_ForAnotherUsersAccountId_ReturnsNotFound()
    {
        using var factory = new AuthApiFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);
        var otherUsersAccount = await InsertAccountForOtherUserAsync(factory, "mBank", "111");

        var response = await PutAccountAsync(client, otherUsersAccount.Id, "mBank", "222");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_RemovesRow()
    {
        using var factory = new AuthApiFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);

        var created = await (await PostAccountAsync(client, "mBank", "111")).Content.ReadFromJsonAsync<AccountDto>(JsonOptions);

        var deleteResponse = await DeleteAccountAsync(client, created!.Id);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var list = await (await client.GetAsync("/api/accounts/")).Content.ReadFromJsonAsync<List<AccountDto>>(JsonOptions);
        Assert.Empty(list!);
    }

    [Fact]
    public async Task Delete_ForAnotherUsersAccountId_ReturnsNotFound()
    {
        using var factory = new AuthApiFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);
        var otherUsersAccount = await InsertAccountForOtherUserAsync(factory, "mBank", "111");

        var response = await DeleteAccountAsync(client, otherUsersAccount.Id);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_ForAccountWithLinkedTransactions_ReturnsConflictWithoutThrowing()
    {
        using var factory = new AuthApiFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);

        var created = await (await PostAccountAsync(client, "mBank", "111")).Content.ReadFromJsonAsync<AccountDto>(JsonOptions);

        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = await userManager.FindByEmailAsync(AuthApiFactory.AllowedEmail);
            var userId = user!.Id;

            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Transactions.Add(new Transaction
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                AccountId = created!.Id,
                Date = new DateOnly(2026, 8, 1),
                Description = "Linked transaction",
                Amount = -10.00m,
                Hash = "irrelevant-for-this-test",
            });
            await db.SaveChangesAsync();
        }

        var deleteResponse = await DeleteAccountAsync(client, created!.Id);

        Assert.Equal(HttpStatusCode.Conflict, deleteResponse.StatusCode);

        var list = await (await client.GetAsync("/api/accounts/")).Content.ReadFromJsonAsync<List<AccountDto>>(JsonOptions);
        Assert.Single(list!);
    }
}
