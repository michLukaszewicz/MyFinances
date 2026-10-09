using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyFinances.Api.Auth;
using MyFinances.Api.Categorization;
using MyFinances.Api.Tests.Auth;
using MyFinances.Api.Transactions;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace MyFinances.Api.Tests.Categorization;

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
        // Arrange
        using var factory = new AuthApiFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);
        // Act
        var response = await client.GetAsync("/api/categorization/categories");
        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var categories = await response.Content.ReadFromJsonAsync<List<CategoryDto>>(JsonOptions);
        Assert.NotNull(categories);
        Assert.Equal(14, categories!.Count);
        Assert.Equal("Groceries", categories[0].Name);
        Assert.Equal("expense", categories[0].Kind);
        Assert.Equal(
            new[] { "Income", "Refunds & Reimbursements", "Other income" },
            categories.Where(c => c.Kind == "income").Select(c => c.Name));
    }

    [Fact]
    public async Task Queue_ExcludesCategorizedAndTransferFlaggedTransactions()
    {
        // Arrange
        using var factory = new AuthApiFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);
        var userId = await GetCurrentUserIdAsync(factory);
        var account = await InsertAccountAsync(factory, userId, "mBank", "111");
        var uncategorized = await InsertTransactionAsync(factory, userId, account.Id, new DateOnly(2026, 8, 1), -20.00m);
        var categorized = await InsertTransactionAsync(factory, userId, account.Id, new DateOnly(2026, 8, 2), -30.00m);
        var categoryId = await GetFirstCategoryIdAsync(client);
        var putResponse = await PutCategorizeAsync(client, categorized.Id, new CategorizeRequest(categoryId, null));
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);
        // Act
        var queue = await (await client.GetAsync("/api/categorization/queue")).Content
            .ReadFromJsonAsync<List<TransactionQueueItemDto>>(JsonOptions);
        // Assert
        Assert.Contains(queue!, t => t.Id == uncategorized.Id);
        Assert.DoesNotContain(queue!, t => t.Id == categorized.Id);
    }

    [Fact]
    public async Task Put_SetsCategory_AndPersistsAcrossSubsequentGet()
    {
        // Arrange
        using var factory = new AuthApiFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);
        var userId = await GetCurrentUserIdAsync(factory);
        var account = await InsertAccountAsync(factory, userId, "mBank", "111");
        var transaction = await InsertTransactionAsync(factory, userId, account.Id, new DateOnly(2026, 8, 1), -20.00m);
        var categoryId = await GetFirstCategoryIdAsync(client);
        // Act
        var putResponse = await PutCategorizeAsync(client, transaction.Id, new CategorizeRequest(categoryId, null));
        // Assert
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);
        var handled = await (await client.GetAsync("/api/categorization/handled")).Content
            .ReadFromJsonAsync<List<TransactionQueueItemDto>>(JsonOptions);
        var handledItem = handled!.Single(t => t.Id == transaction.Id);
        Assert.Equal(categoryId, handledItem.CategoryId);
    }

    [Fact]
    public async Task Put_SetsInternalTransfer_PersistsAndSurvivesLaterDetectionPass()
    {
        // Arrange
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
        // Act
        // Trigger a detection pass via GET /queue.
        var queue = await (await client.GetAsync("/api/categorization/queue")).Content
            .ReadFromJsonAsync<List<TransactionQueueItemDto>>(JsonOptions);
        // Assert
        // legA's manual "not a transfer" decision must survive detection and it stays in the queue.
        Assert.Contains(queue!, t => t.Id == legA.Id && !t.IsInternalTransfer);
    }

    [Fact]
    public async Task Queue_And_Handled_RunDetectionAndAutoFlagMatchingPair()
    {
        // Arrange
        using var factory = new AuthApiFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);
        var userId = await GetCurrentUserIdAsync(factory);
        var accountA = await InsertAccountAsync(factory, userId, "mBank", "111");
        var accountB = await InsertAccountAsync(factory, userId, "mBank", "222");
        var legA = await InsertTransactionAsync(factory, userId, accountA.Id, new DateOnly(2026, 8, 1), -500.00m);
        var legB = await InsertTransactionAsync(factory, userId, accountB.Id, new DateOnly(2026, 8, 2), 500.00m);
        // Act
        var queue = await (await client.GetAsync("/api/categorization/queue")).Content
            .ReadFromJsonAsync<List<TransactionQueueItemDto>>(JsonOptions);
        // Assert
        Assert.DoesNotContain(queue!, t => t.Id == legA.Id || t.Id == legB.Id);
        var handled = await (await client.GetAsync("/api/categorization/handled")).Content
            .ReadFromJsonAsync<List<TransactionQueueItemDto>>(JsonOptions);
        Assert.Contains(handled!, t => t.Id == legA.Id && t.IsInternalTransfer);
        Assert.Contains(handled!, t => t.Id == legB.Id && t.IsInternalTransfer);
    }

    [Fact]
    public async Task CrossUserIsolation_TransactionFromAnotherUserNeverAppears()
    {
        // Arrange
        using var factory = new AuthApiFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);
        var otherUserId = Guid.NewGuid();
        var otherAccount = await InsertAccountAsync(factory, otherUserId, "mBank", "999");
        var otherTransaction = await InsertTransactionAsync(factory, otherUserId, otherAccount.Id, new DateOnly(2026, 8, 1), -10.00m);
        // Act
        var queue = await (await client.GetAsync("/api/categorization/queue")).Content
            .ReadFromJsonAsync<List<TransactionQueueItemDto>>(JsonOptions);
        var handled = await (await client.GetAsync("/api/categorization/handled")).Content
            .ReadFromJsonAsync<List<TransactionQueueItemDto>>(JsonOptions);
        // Assert
        Assert.DoesNotContain(queue!, t => t.Id == otherTransaction.Id);
        Assert.DoesNotContain(handled!, t => t.Id == otherTransaction.Id);
        var putResponse = await PutCategorizeAsync(client, otherTransaction.Id, new CategorizeRequest(await GetFirstCategoryIdAsync(client), null));
        Assert.Equal(HttpStatusCode.NotFound, putResponse.StatusCode);
    }

    private static async Task<HttpResponseMessage> SendWithTokenAsync(HttpClient client, HttpMethod method, string url, object? body = null)
    {
        var token = await GetAntiforgeryTokenAsync(client);
        using var httpRequest = new HttpRequestMessage(method, url);
        if (body is not null)
        {
            httpRequest.Content = JsonContent.Create(body);
        }
        httpRequest.Headers.Add("X-XSRF-TOKEN", token);
        return await client.SendAsync(httpRequest);
    }

    private static async Task<CategoryDto> CreateCategoryAsync(HttpClient client, string name, string kind = "expense")
    {
        var response = await SendWithTokenAsync(client, HttpMethod.Post, "/api/categorization/categories", new CategoryWriteRequest(name, kind));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CategoryDto>(JsonOptions))!;
    }

    private static async Task<List<TransactionQueueItemDto>> GetQueueAsync(HttpClient client) =>
        (await (await client.GetAsync("/api/categorization/queue")).Content.ReadFromJsonAsync<List<TransactionQueueItemDto>>(JsonOptions))!;

    [Fact]
    public async Task PostCategory_CreatesCategoryListedAfterTheStarterSet()
    {
        // Arrange
        using var factory = new AuthApiFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);
        // Act
        var created = await CreateCategoryAsync(client, "  Pets  ", "Income");
        // Assert
        Assert.Equal("Pets", created.Name);
        Assert.Equal("income", created.Kind);
        var categories = await (await client.GetAsync("/api/categorization/categories")).Content
            .ReadFromJsonAsync<List<CategoryDto>>(JsonOptions);
        Assert.Equal(15, categories!.Count);
        Assert.Equal(created.Id, categories.Last().Id);
    }

    [Fact]
    public async Task PostCategory_AssignsTheLowestFreeColorSlot()
    {
        // Arrange
        using var factory = new AuthApiFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);
        var starter = await (await client.GetAsync("/api/categorization/categories")).Content
            .ReadFromJsonAsync<List<CategoryDto>>(JsonOptions);
        Assert.Equal(Enumerable.Range(0, 14), starter!.Select(c => c.ColorSlot).OrderBy(slot => slot));
        var freed = starter.Single(c => c.ColorSlot == 3);
        await SendWithTokenAsync(client, HttpMethod.Delete, $"/api/categorization/categories/{freed.Id}");
        // Act
        var first = await CreateCategoryAsync(client, "Pets");
        var second = await CreateCategoryAsync(client, "Hobbies");
        // Assert
        Assert.Equal(3, first.ColorSlot);
        Assert.Equal(14, second.ColorSlot);
    }

    [Theory]
    [InlineData("", "expense")]
    [InlineData("   ", "expense")]
    [InlineData("Pets", "savings")]
    [InlineData("Pets", "")]
    public async Task PostCategory_WithInvalidNameOrKind_IsBadRequest(string name, string kind)
    {
        // Arrange
        using var factory = new AuthApiFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);
        // Act
        var response = await SendWithTokenAsync(client, HttpMethod.Post, "/api/categorization/categories", new CategoryWriteRequest(name, kind));
        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostCategory_WithTooLongName_IsBadRequest()
    {
        // Arrange
        using var factory = new AuthApiFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);
        // Act
        var response = await SendWithTokenAsync(
            client, HttpMethod.Post, "/api/categorization/categories", new CategoryWriteRequest(new string('a', 51), "expense"));
        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("groceries")]
    [InlineData("Pets")]
    public async Task PostCategory_WithExistingNameIgnoringCase_IsConflict(string name)
    {
        // Arrange
        using var factory = new AuthApiFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);
        await CreateCategoryAsync(client, "Pets");
        // Act
        var response = await SendWithTokenAsync(client, HttpMethod.Post, "/api/categorization/categories", new CategoryWriteRequest(name, "expense"));
        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task DeleteCategory_Unused_RemovesIt()
    {
        // Arrange
        using var factory = new AuthApiFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);
        var category = await CreateCategoryAsync(client, "Pets");
        // Act
        var response = await SendWithTokenAsync(client, HttpMethod.Delete, $"/api/categorization/categories/{category.Id}");
        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var categories = await (await client.GetAsync("/api/categorization/categories")).Content
            .ReadFromJsonAsync<List<CategoryDto>>(JsonOptions);
        Assert.DoesNotContain(categories!, c => c.Id == category.Id);
    }

    [Fact]
    public async Task DeleteCategory_UsedByTransactions_IsConflictWithCountAndChangesNothing()
    {
        // Arrange
        using var factory = new AuthApiFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);
        var userId = await GetCurrentUserIdAsync(factory);
        var account = await InsertAccountAsync(factory, userId, "mBank", "111");
        var category = await CreateCategoryAsync(client, "Pets");
        var first = await InsertTransactionAsync(factory, userId, account.Id, new DateOnly(2026, 8, 1), -20.00m);
        var second = await InsertTransactionAsync(factory, userId, account.Id, new DateOnly(2026, 8, 2), -30.00m);
        await PutCategorizeAsync(client, first.Id, new CategorizeRequest(category.Id, null));
        await PutCategorizeAsync(client, second.Id, new CategorizeRequest(category.Id, null));
        // Act
        var response = await SendWithTokenAsync(client, HttpMethod.Delete, $"/api/categorization/categories/{category.Id}");
        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal(2, body.GetProperty("transactionCount").GetInt32());
        Assert.Empty(await GetQueueAsync(client));
        var categories = await (await client.GetAsync("/api/categorization/categories")).Content
            .ReadFromJsonAsync<List<CategoryDto>>(JsonOptions);
        Assert.Contains(categories!, c => c.Id == category.Id);
    }

    [Fact]
    public async Task DeleteCategory_UsedByTransactions_WhenConfirmed_MovesThemBackToTheQueue()
    {
        // Arrange
        using var factory = new AuthApiFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);
        var userId = await GetCurrentUserIdAsync(factory);
        var account = await InsertAccountAsync(factory, userId, "mBank", "111");
        var category = await CreateCategoryAsync(client, "Pets");
        var builtInCategoryId = await GetFirstCategoryIdAsync(client);
        var usingCustom = await InsertTransactionAsync(factory, userId, account.Id, new DateOnly(2026, 8, 1), -20.00m);
        var usingBuiltIn = await InsertTransactionAsync(factory, userId, account.Id, new DateOnly(2026, 8, 2), -30.00m);
        await PutCategorizeAsync(client, usingCustom.Id, new CategorizeRequest(category.Id, null));
        await PutCategorizeAsync(client, usingBuiltIn.Id, new CategorizeRequest(builtInCategoryId, null));
        // Act
        var response = await SendWithTokenAsync(
            client, HttpMethod.Delete, $"/api/categorization/categories/{category.Id}?uncategorizeTransactions=true");
        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var queue = await GetQueueAsync(client);
        Assert.Equal(usingCustom.Id, Assert.Single(queue).Id);
        Assert.Null(queue[0].CategoryId);
        var handled = await (await client.GetAsync("/api/categorization/handled")).Content
            .ReadFromJsonAsync<List<TransactionQueueItemDto>>(JsonOptions);
        Assert.Equal(usingBuiltIn.Id, Assert.Single(handled!).Id);
    }

    [Fact]
    public async Task DeleteCategory_StarterCategoryUsedByTransactions_IsConflictUntilConfirmed()
    {
        // Arrange
        using var factory = new AuthApiFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);
        var userId = await GetCurrentUserIdAsync(factory);
        var account = await InsertAccountAsync(factory, userId, "mBank", "111");
        var builtInCategoryId = await GetFirstCategoryIdAsync(client);
        var transaction = await InsertTransactionAsync(factory, userId, account.Id, new DateOnly(2026, 8, 1), -20.00m);
        await PutCategorizeAsync(client, transaction.Id, new CategorizeRequest(builtInCategoryId, null));
        // Act
        var unconfirmed = await SendWithTokenAsync(client, HttpMethod.Delete, $"/api/categorization/categories/{builtInCategoryId}");
        var confirmed = await SendWithTokenAsync(
            client,
            HttpMethod.Delete,
            $"/api/categorization/categories/{builtInCategoryId}?uncategorizeTransactions=true");
        // Assert
        Assert.Equal(HttpStatusCode.Conflict, unconfirmed.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, confirmed.StatusCode);
        Assert.Equal(transaction.Id, Assert.Single(await GetQueueAsync(client)).Id);
    }

    [Fact]
    public async Task DeleteCategory_StarterCategory_RemovesItAndAllowsRecreatingTheName()
    {
        // Arrange
        using var factory = new AuthApiFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);
        var starterCategory = (await (await client.GetAsync("/api/categorization/categories")).Content
            .ReadFromJsonAsync<List<CategoryDto>>(JsonOptions))!.First();
        // Act
        var response = await SendWithTokenAsync(client, HttpMethod.Delete, $"/api/categorization/categories/{starterCategory.Id}");
        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var categories = await (await client.GetAsync("/api/categorization/categories")).Content
            .ReadFromJsonAsync<List<CategoryDto>>(JsonOptions);
        Assert.Equal(13, categories!.Count);
        Assert.DoesNotContain(categories, c => c.Id == starterCategory.Id);
        var recreated = await CreateCategoryAsync(client, starterCategory.Name);
        Assert.Equal(starterCategory.Name, recreated.Name);
    }

    [Fact]
    public async Task PutCategorize_WithADeletedCategory_IsBadRequest()
    {
        // Arrange
        using var factory = new AuthApiFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);
        var userId = await GetCurrentUserIdAsync(factory);
        var account = await InsertAccountAsync(factory, userId, "mBank", "111");
        var transaction = await InsertTransactionAsync(factory, userId, account.Id, new DateOnly(2026, 8, 1), -20.00m);
        var builtInCategoryId = await GetFirstCategoryIdAsync(client);
        await SendWithTokenAsync(client, HttpMethod.Delete, $"/api/categorization/categories/{builtInCategoryId}");
        // Act
        var response = await PutCategorizeAsync(client, transaction.Id, new CategorizeRequest(builtInCategoryId, null));
        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeleteCategory_Unknown_IsNotFound()
    {
        // Arrange
        using var factory = new AuthApiFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);
        // Act
        var response = await SendWithTokenAsync(client, HttpMethod.Delete, $"/api/categorization/categories/{Guid.NewGuid()}");
        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task QueueCount_CountsOnlyUncategorizedNonTransferTransactionsOfTheUser()
    {
        // Arrange
        using var factory = new AuthApiFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);
        var userId = await GetCurrentUserIdAsync(factory);
        var account = await InsertAccountAsync(factory, userId, "mBank", "111");
        await InsertTransactionAsync(factory, userId, account.Id, new DateOnly(2026, 8, 1), -20.00m);
        await InsertTransactionAsync(factory, userId, account.Id, new DateOnly(2026, 8, 2), -30.00m);
        var categorized = await InsertTransactionAsync(factory, userId, account.Id, new DateOnly(2026, 8, 3), -40.00m);
        await PutCategorizeAsync(client, categorized.Id, new CategorizeRequest(await GetFirstCategoryIdAsync(client), null));
        var otherAccount = await InsertAccountAsync(factory, Guid.NewGuid(), "mBank", "999");
        await InsertTransactionAsync(factory, otherAccount.UserId, otherAccount.Id, new DateOnly(2026, 8, 1), -10.00m);
        // Act
        var response = await client.GetAsync("/api/categorization/queue/count");
        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<UncategorizedCountDto>(JsonOptions);
        Assert.Equal(2, body!.Count);
    }
}