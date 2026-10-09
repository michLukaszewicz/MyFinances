using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyFinances.Api.Auth;
using MyFinances.Api.Tests.Auth;
using MyFinances.Api.Transactions;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace MyFinances.Api.Tests.Accounts;

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

    private static async Task<HttpResponseMessage> PostAccountAsync(HttpClient client, string bank, string number, string? bankChoice = null)
    {
        var token = await GetAntiforgeryTokenAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/accounts/")
        {
            Content = JsonContent.Create(new AccountWriteRequest(bank, number, bankChoice)),
        };
        request.Headers.Add("X-XSRF-TOKEN", token);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PutAccountAsync(HttpClient client, Guid id, string bank, string number, string? bankChoice = null)
    {
        var token = await GetAntiforgeryTokenAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/accounts/{id}")
        {
            Content = JsonContent.Create(new AccountWriteRequest(bank, number, bankChoice)),
        };
        request.Headers.Add("X-XSRF-TOKEN", token);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> DeleteAccountAsync(HttpClient client, Guid id, bool deleteTransactions = false)
    {
        var token = await GetAntiforgeryTokenAsync(client);
        var query = deleteTransactions ? "?deleteTransactions=true" : string.Empty;
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/accounts/{id}{query}");
        request.Headers.Add("X-XSRF-TOKEN", token);
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task GetBanks_ReturnsPolishBanksThenOther_IncludingEveryParserBank()
    {
        // Arrange
        using var factory = new AuthApiFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);
        // Act
        var response = await client.GetAsync("/api/accounts/banks");
        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<BankOptionsResponse>(JsonOptions);
        Assert.NotNull(payload);
        Assert.Equal("Other", payload!.BankNames[^1]);
        Assert.Equal(payload.BankNames.Count, payload.BankNames.Distinct().Count());
        // The import mismatch check compares parser names with the dropdown value, so each
        // parser's BankName must be selectable verbatim.
        using var scope = factory.Services.CreateScope();
        foreach (var parser in scope.ServiceProvider.GetServices<MyFinances.Api.Import.IBankStatementParser>())
        {
            Assert.Contains(parser.BankName, payload.BankNames);
        }
    }

    [Fact]
    public async Task Create_WithBankFromTheList_StoresItAndKeepsTheFreeTextLabel()
    {
        // Arrange
        using var factory = new AuthApiFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);
        // Act
        var response = await PostAccountAsync(client, "Konto na codzień", "111", "velobank");
        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<AccountDto>(JsonOptions);
        Assert.Equal("Konto na codzień", dto!.BankName);
        Assert.Equal("VeloBank", dto.Bank);
    }

    [Fact]
    public async Task Create_WithBankNotInTheList_ReturnsBadRequest()
    {
        // Arrange
        using var factory = new AuthApiFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);
        // Act
        var response = await PostAccountAsync(client, "My account", "111", "velobandk");
        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithoutBank_DerivesItFromTheNameWhenItIsAKnownBank_ElseOther()
    {
        // Arrange
        using var factory = new AuthApiFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);
        // Act
        var known = await (await PostAccountAsync(client, "mbank", "111")).Content.ReadFromJsonAsync<AccountDto>(JsonOptions);
        var unknown = await (await PostAccountAsync(client, "velobandk", "222")).Content.ReadFromJsonAsync<AccountDto>(JsonOptions);
        // Assert
        Assert.Equal("mBank", known!.Bank);
        Assert.Equal("Other", unknown!.Bank);
    }

    [Fact]
    public async Task Update_ChangesTheBank_AndRejectsOneNotInTheList()
    {
        // Arrange
        using var factory = new AuthApiFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);
        var created = await (await PostAccountAsync(client, "velobandk", "111")).Content.ReadFromJsonAsync<AccountDto>(JsonOptions);
        // Act
        var bad = await PutAccountAsync(client, created!.Id, "velobandk", "111", "nope");
        var ok = await PutAccountAsync(client, created.Id, "velobandk", "111", "VeloBank");
        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var updated = await ok.Content.ReadFromJsonAsync<AccountDto>(JsonOptions);
        Assert.Equal("VeloBank", updated!.Bank);
        Assert.Equal("velobandk", updated.BankName);
    }

    [Fact]
    public async Task Create_WithNewBankAndNumber_Inserts()
    {
        // Arrange
        using var factory = new AuthApiFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);
        // Act
        var response = await PostAccountAsync(client, "mBank", "12345678901234567890123456");
        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<AccountDto>(JsonOptions);
        Assert.NotNull(dto);
        Assert.Equal("mBank", dto!.BankName);
    }

    [Fact]
    public async Task Create_WithDuplicateBankAndNumberForSameUser_ReturnsConflictWithoutInserting()
    {
        // Arrange
        using var factory = new AuthApiFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);
        var first = await PostAccountAsync(client, "mBank", "111");
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        // Act
        var second = await PostAccountAsync(client, "mBank", "111");
        // Assert
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var list = await (await client.GetAsync("/api/accounts/")).Content.ReadFromJsonAsync<List<AccountDto>>(JsonOptions);
        Assert.Single(list!);
    }

    [Fact]
    public async Task Create_WithSameBankAndNumberForDifferentUser_Succeeds()
    {
        // Arrange
        using var factory = new AuthApiFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);
        await InsertAccountForOtherUserAsync(factory, "mBank", "999");
        // Act
        var response = await PostAccountAsync(client, "mBank", "999");
        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task List_ScopedToCurrentUserOnly()
    {
        // Arrange
        using var factory = new AuthApiFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);
        await PostAccountAsync(client, "mBank", "111");
        await InsertAccountForOtherUserAsync(factory, "mBank", "222");
        // Act
        var list = await (await client.GetAsync("/api/accounts/")).Content.ReadFromJsonAsync<List<AccountDto>>(JsonOptions);
        // Assert
        Assert.Single(list!);
        Assert.Equal("111", list![0].AccountNumber);
    }

    [Fact]
    public async Task Update_RecomputesFieldsAndExcludesSelfFromDuplicateCheck()
    {
        // Arrange
        using var factory = new AuthApiFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);
        var created = await (await PostAccountAsync(client, "mBank", "111")).Content.ReadFromJsonAsync<AccountDto>(JsonOptions);
        // Act
        var updateResponse = await PutAccountAsync(client, created!.Id, "mBank", "111");
        // Assert
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
        // Arrange
        using var factory = new AuthApiFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);
        var first = await (await PostAccountAsync(client, "mBank", "111")).Content.ReadFromJsonAsync<AccountDto>(JsonOptions);
        var second = await (await PostAccountAsync(client, "mBank", "222")).Content.ReadFromJsonAsync<AccountDto>(JsonOptions);
        // Act
        var response = await PutAccountAsync(client, second!.Id, "mBank", "111");
        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Update_ForAnotherUsersAccountId_ReturnsNotFound()
    {
        // Arrange
        using var factory = new AuthApiFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);
        var otherUsersAccount = await InsertAccountForOtherUserAsync(factory, "mBank", "111");
        // Act
        var response = await PutAccountAsync(client, otherUsersAccount.Id, "mBank", "222");
        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_RemovesRow()
    {
        // Arrange
        using var factory = new AuthApiFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);
        var created = await (await PostAccountAsync(client, "mBank", "111")).Content.ReadFromJsonAsync<AccountDto>(JsonOptions);
        // Act
        var deleteResponse = await DeleteAccountAsync(client, created!.Id);
        // Assert
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        var list = await (await client.GetAsync("/api/accounts/")).Content.ReadFromJsonAsync<List<AccountDto>>(JsonOptions);
        Assert.Empty(list!);
    }

    [Fact]
    public async Task Delete_ForAnotherUsersAccountId_ReturnsNotFound()
    {
        // Arrange
        using var factory = new AuthApiFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);
        var otherUsersAccount = await InsertAccountForOtherUserAsync(factory, "mBank", "111");
        // Act
        var response = await DeleteAccountAsync(client, otherUsersAccount.Id);
        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_ForAccountWithLinkedTransactions_ReturnsConflictWithoutThrowing()
    {
        // Arrange
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
        // Act
        var deleteResponse = await DeleteAccountAsync(client, created!.Id);
        // Assert
        Assert.Equal(HttpStatusCode.Conflict, deleteResponse.StatusCode);
        using var problem = JsonDocument.Parse(await deleteResponse.Content.ReadAsStringAsync());
        Assert.Equal(1, problem.RootElement.GetProperty("transactionCount").GetInt32());
        var list = await (await client.GetAsync("/api/accounts/")).Content.ReadFromJsonAsync<List<AccountDto>>(JsonOptions);
        Assert.Single(list!);
    }

    [Fact]
    public async Task Delete_WithDeleteTransactions_RemovesAccountItsTransactionsAndImportBatches()
    {
        // Arrange
        using var factory = new AuthApiFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);
        var created = await (await PostAccountAsync(client, "mBank", "111")).Content.ReadFromJsonAsync<AccountDto>(JsonOptions);
        var otherAccount = await (await PostAccountAsync(client, "mBank", "222")).Content.ReadFromJsonAsync<AccountDto>(JsonOptions);
        var batchId = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = await userManager.FindByEmailAsync(AuthApiFactory.AllowedEmail);
            var userId = user!.Id;
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ImportBatches.Add(new ImportBatch { Id = batchId, UserId = userId, AccountId = created!.Id, ImportedAtUtc = DateTime.UtcNow });
            db.Transactions.Add(new Transaction
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                AccountId = created.Id,
                Date = new DateOnly(2026, 8, 1),
                Description = "Deleted with the account",
                Amount = -10.00m,
                Hash = "hash-a",
                ImportBatchId = batchId,
            });
            db.Transactions.Add(new Transaction
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                AccountId = otherAccount!.Id,
                Date = new DateOnly(2026, 8, 2),
                Description = "Moved here, keeps the old batch id",
                Amount = -20.00m,
                Hash = "hash-b",
                ImportBatchId = batchId,
            });
            await db.SaveChangesAsync();
        }
        // Act
        var deleteResponse = await DeleteAccountAsync(client, created!.Id, deleteTransactions: true);
        // Assert
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        using var assertScope = factory.Services.CreateScope();
        var assertDb = assertScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(await assertDb.ImportBatches.ToListAsync());
        var remaining = await assertDb.Transactions.ToListAsync();
        var survivor = Assert.Single(remaining);
        Assert.Equal(otherAccount!.Id, survivor.AccountId);
        Assert.Null(survivor.ImportBatchId);
    }
}