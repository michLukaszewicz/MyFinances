using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyFinances.Api.Auth;
using MyFinances.Api.Categorization;
using MyFinances.Api.Transactions;
using Xunit;

namespace MyFinances.Api.Tests;

// Integration coverage for CategorizationEndpoints: reuses AuthApiFactory's WebApplicationFactory
// + EF Core InMemory swap (same pattern as AccountEndpointsTests). The seeded Category HasData
// rows are applied automatically by the InMemory provider on first database use.
public class CategorizationEndpointsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static async Task<HttpClient> CreateAuthenticatedClientAsync(AuthApiFactory factory)
    {
        var client = factory.CreateClient();
        var registerResponse = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(AuthApiFactory.AllowedEmail, "correct-horse-battery"));
        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);
        return client;
    }

    private static async Task<Guid> GetCurrentUserIdAsync(AuthApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = await userManager.FindByEmailAsync(AuthApiFactory.AllowedEmail);
        return user!.Id;
    }

    private static async Task<Account> InsertAccountAsync(AuthApiFactory factory, Guid userId, string bank, string number)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var account = new Account { Id = Guid.NewGuid(), UserId = userId, BankName = bank, AccountNumber = number };
        db.Accounts.Add(account);
        await db.SaveChangesAsync();
        return account;
    }

    private static async Task<Transaction> InsertTransactionAsync(
        AuthApiFactory factory, Guid userId, Guid accountId, DateOnly date, decimal amount, string description = "txn")
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            AccountId = accountId,
            Date = date,
            Description = description,
            Amount = amount,
            Hash = Guid.NewGuid().ToString(),
        };
        db.Transactions.Add(transaction);
        await db.SaveChangesAsync();
        return transaction;
    }

    private static async Task<Guid> GetFirstCategoryIdAsync(HttpClient client)
    {
        var categories = await (await client.GetAsync("/api/categorization/categories"))
            .Content.ReadFromJsonAsync<List<CategoryDto>>(JsonOptions);
        return categories!.First().Id;
    }

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/auth/antiforgery-token");
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<TokenResponse>(JsonOptions);
        return payload!.Token;
    }

    private record TokenResponse(string Token);

    private static async Task<HttpResponseMessage> PutCategorizeAsync(HttpClient client, Guid id, CategorizeRequest request)
    {
        var token = await GetAntiforgeryTokenAsync(client);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/categorization/transactions/{id}")
        {
            Content = JsonContent.Create(request),
        };
        httpRequest.Headers.Add("X-XSRF-TOKEN", token);
        return await client.SendAsync(httpRequest);
    }

    [Fact]
    public async Task GetCategories_ReturnsSeededListOrderedBySortOrder()
    {
        using var factory = new AuthApiFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);

        var response = await client.GetAsync("/api/categorization/categories");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var categories = await response.Content.ReadFromJsonAsync<List<CategoryDto>>(JsonOptions);
        Assert.NotNull(categories);
        Assert.Equal(12, categories!.Count);
        Assert.Equal("Groceries", categories[0].Name);
    }

    [Fact]
    public async Task Queue_ExcludesCategorizedAndTransferFlaggedTransactions()
    {
        using var factory = new AuthApiFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);
        var userId = await GetCurrentUserIdAsync(factory);
        var account = await InsertAccountAsync(factory, userId, "mBank", "111");

        var uncategorized = await InsertTransactionAsync(factory, userId, account.Id, new DateOnly(2026, 8, 1), -20.00m);
        var categorized = await InsertTransactionAsync(factory, userId, account.Id, new DateOnly(2026, 8, 2), -30.00m);
        var categoryId = await GetFirstCategoryIdAsync(client);
        var putResponse = await PutCategorizeAsync(client, categorized.Id, new CategorizeRequest(categoryId, null));
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);

        var queue = await (await client.GetAsync("/api/categorization/queue")).Content
            .ReadFromJsonAsync<List<TransactionQueueItemDto>>(JsonOptions);

        Assert.Contains(queue!, t => t.Id == uncategorized.Id);
        Assert.DoesNotContain(queue!, t => t.Id == categorized.Id);
    }

    [Fact]
    public async Task Put_SetsCategory_AndPersistsAcrossSubsequentGet()
    {
        using var factory = new AuthApiFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);
        var userId = await GetCurrentUserIdAsync(factory);
        var account = await InsertAccountAsync(factory, userId, "mBank", "111");
        var transaction = await InsertTransactionAsync(factory, userId, account.Id, new DateOnly(2026, 8, 1), -20.00m);
        var categoryId = await GetFirstCategoryIdAsync(client);

        var putResponse = await PutCategorizeAsync(client, transaction.Id, new CategorizeRequest(categoryId, null));
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);

        var handled = await (await client.GetAsync("/api/categorization/handled")).Content
            .ReadFromJsonAsync<List<TransactionQueueItemDto>>(JsonOptions);
        var handledItem = handled!.Single(t => t.Id == transaction.Id);
        Assert.Equal(categoryId, handledItem.CategoryId);
    }

    [Fact]
    public async Task Put_SetsInternalTransfer_PersistsAndSurvivesLaterDetectionPass()
    {
        using var factory = new AuthApiFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);
        var userId = await GetCurrentUserIdAsync(factory);
        var accountA = await InsertAccountAsync(factory, userId, "mBank", "111");
        var accountB = await InsertAccountAsync(factory, userId, "mBank", "222");

        // A pair that would otherwise be auto-flagged by detection.
        var legA = await InsertTransactionAsync(factory, userId, accountA.Id, new DateOnly(2026, 8, 1), -500.00m);
        var legB = await InsertTransactionAsync(factory, userId, accountB.Id, new DateOnly(2026, 8, 1), 500.00m);

        // Manually override legA back to "not a transfer" before any detection pass runs.
        var putResponse = await PutCategorizeAsync(client, legA.Id, new CategorizeRequest(null, false));
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);

        // Trigger a detection pass via GET /queue.
        var queue = await (await client.GetAsync("/api/categorization/queue")).Content
            .ReadFromJsonAsync<List<TransactionQueueItemDto>>(JsonOptions);

        // legA's manual "not a transfer" decision must survive detection and it stays in the queue.
        Assert.Contains(queue!, t => t.Id == legA.Id && !t.IsInternalTransfer);
    }

    [Fact]
    public async Task Queue_And_Handled_RunDetectionAndAutoFlagMatchingPair()
    {
        using var factory = new AuthApiFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);
        var userId = await GetCurrentUserIdAsync(factory);
        var accountA = await InsertAccountAsync(factory, userId, "mBank", "111");
        var accountB = await InsertAccountAsync(factory, userId, "mBank", "222");

        var legA = await InsertTransactionAsync(factory, userId, accountA.Id, new DateOnly(2026, 8, 1), -500.00m);
        var legB = await InsertTransactionAsync(factory, userId, accountB.Id, new DateOnly(2026, 8, 2), 500.00m);

        var queue = await (await client.GetAsync("/api/categorization/queue")).Content
            .ReadFromJsonAsync<List<TransactionQueueItemDto>>(JsonOptions);
        Assert.DoesNotContain(queue!, t => t.Id == legA.Id || t.Id == legB.Id);

        var handled = await (await client.GetAsync("/api/categorization/handled")).Content
            .ReadFromJsonAsync<List<TransactionQueueItemDto>>(JsonOptions);
        Assert.Contains(handled!, t => t.Id == legA.Id && t.IsInternalTransfer);
        Assert.Contains(handled!, t => t.Id == legB.Id && t.IsInternalTransfer);
    }

    [Fact]
    public async Task CrossUserIsolation_TransactionFromAnotherUserNeverAppears()
    {
        using var factory = new AuthApiFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);
        var otherUserId = Guid.NewGuid();
        var otherAccount = await InsertAccountAsync(factory, otherUserId, "mBank", "999");
        var otherTransaction = await InsertTransactionAsync(factory, otherUserId, otherAccount.Id, new DateOnly(2026, 8, 1), -10.00m);

        var queue = await (await client.GetAsync("/api/categorization/queue")).Content
            .ReadFromJsonAsync<List<TransactionQueueItemDto>>(JsonOptions);
        var handled = await (await client.GetAsync("/api/categorization/handled")).Content
            .ReadFromJsonAsync<List<TransactionQueueItemDto>>(JsonOptions);

        Assert.DoesNotContain(queue!, t => t.Id == otherTransaction.Id);
        Assert.DoesNotContain(handled!, t => t.Id == otherTransaction.Id);

        var putResponse = await PutCategorizeAsync(client, otherTransaction.Id, new CategorizeRequest(await GetFirstCategoryIdAsync(client), null));
        Assert.Equal(HttpStatusCode.NotFound, putResponse.StatusCode);
    }
}
