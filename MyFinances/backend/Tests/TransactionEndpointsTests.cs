using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyFinances.Api;
using MyFinances.Api.Auth;
using MyFinances.Api.Categorization;
using MyFinances.Api.Import;
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

    private static async Task<Guid> GetFirstCategoryIdAsync(HttpClient client)
    {
        var categories = await (await client.GetAsync("/api/categorization/categories"))
            .Content.ReadFromJsonAsync<List<CategoryDto>>(JsonOptions);
        return categories!.First().Id;
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

    private static async Task<Transaction> InsertTransactionAsync(
        AuthApiFactory factory,
        Guid userId,
        Guid accountId,
        DateOnly date,
        decimal amount,
        string description = "txn",
        Guid? importBatchId = null,
        string? hash = null)
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
            Hash = hash ?? Guid.NewGuid().ToString(),
            ImportBatchId = importBatchId,
        };
        db.Transactions.Add(transaction);
        await db.SaveChangesAsync();
        return transaction;
    }

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/auth/antiforgery-token");
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<TokenResponse>(JsonOptions);
        return payload!.Token;
    }

    private static async Task<HttpResponseMessage> PostTransactionAsync(HttpClient client, TransactionWriteRequest request)
    {
        var token = await GetAntiforgeryTokenAsync(client);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/transactions/")
        {
            Content = JsonContent.Create(request),
        };
        httpRequest.Headers.Add("X-XSRF-TOKEN", token);
        return await client.SendAsync(httpRequest);
    }

    private static async Task<HttpResponseMessage> PutTransactionAsync(HttpClient client, Guid id, TransactionWriteRequest request)
    {
        var token = await GetAntiforgeryTokenAsync(client);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/transactions/{id}")
        {
            Content = JsonContent.Create(request),
        };
        httpRequest.Headers.Add("X-XSRF-TOKEN", token);
        return await client.SendAsync(httpRequest);
    }

    private static async Task<HttpResponseMessage> DeleteTransactionAsync(HttpClient client, Guid id)
    {
        var token = await GetAntiforgeryTokenAsync(client);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/transactions/{id}");
        httpRequest.Headers.Add("X-XSRF-TOKEN", token);
        return await client.SendAsync(httpRequest);
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

    private static async Task SeedCategorizedTransactionAsync(
        AuthApiFactory factory, Guid userId, Guid accountId, DateOnly date, Guid? categoryId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Transactions.Add(new Transaction
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            AccountId = accountId,
            Date = date,
            Description = "txn",
            Amount = -10m,
            Hash = Guid.NewGuid().ToString(),
            CategoryId = categoryId,
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task List_WithCategoryId_ReturnsOnlyThatCategoryAndFilteredHasMore()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var account = await CreateAccountAsync(client, "mBank", "111");
        var userId = await GetUserIdAsync(factory);
        var categories = await (await client.GetAsync("/api/categorization/categories"))
            .Content.ReadFromJsonAsync<List<CategoryDto>>(JsonOptions);
        var target = categories![0].Id;
        var other = categories[1].Id;
        var date = CurrentMonthRange.Get().Start;
        for (var i = 0; i < 3; i++)
        {
            await SeedCategorizedTransactionAsync(factory, userId, account.Id, date, target);
        }

        await SeedCategorizedTransactionAsync(factory, userId, account.Id, date, other);
        await SeedCategorizedTransactionAsync(factory, userId, account.Id, date, null);

        var page = await (await client.GetAsync($"/api/transactions?categoryId={target}&skip=0&take=2")).Content
            .ReadFromJsonAsync<TransactionListResponseDto>(JsonOptions);
        var lastPage = await (await client.GetAsync($"/api/transactions?categoryId={target}&skip=2&take=2")).Content
            .ReadFromJsonAsync<TransactionListResponseDto>(JsonOptions);

        Assert.Equal(2, page!.Items.Count);
        Assert.All(page.Items, i => Assert.Equal(target, i.CategoryId));
        Assert.True(page.HasMore);
        Assert.Single(lastPage!.Items);
        Assert.False(lastPage.HasMore);
    }

    [Fact]
    public async Task List_WithCurrentMonthTrue_ExcludesTransactionsOutsideCurrentMonth()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var account = await CreateAccountAsync(client, "mBank", "111");
        var userId = await GetUserIdAsync(factory);
        var (start, end) = CurrentMonthRange.Get();
        await SeedCategorizedTransactionAsync(factory, userId, account.Id, start, null);
        await SeedCategorizedTransactionAsync(factory, userId, account.Id, end, null);
        await SeedCategorizedTransactionAsync(factory, userId, account.Id, start.AddDays(-1), null);
        await SeedCategorizedTransactionAsync(factory, userId, account.Id, end.AddDays(1), null);

        var filtered = await (await client.GetAsync("/api/transactions?currentMonth=true&take=100")).Content
            .ReadFromJsonAsync<TransactionListResponseDto>(JsonOptions);
        var unfiltered = await (await client.GetAsync("/api/transactions?take=100")).Content
            .ReadFromJsonAsync<TransactionListResponseDto>(JsonOptions);

        Assert.Equal(2, filtered!.Items.Count);
        Assert.All(filtered.Items, i => Assert.InRange(i.Date, start, end));
        Assert.Equal(4, unfiltered!.Items.Count);
    }

    [Fact]
    public async Task List_WithCategoryIdAndCurrentMonth_AppliesBothFilters()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var account = await CreateAccountAsync(client, "mBank", "111");
        var userId = await GetUserIdAsync(factory);
        var categories = await (await client.GetAsync("/api/categorization/categories"))
            .Content.ReadFromJsonAsync<List<CategoryDto>>(JsonOptions);
        var target = categories![0].Id;
        var other = categories[1].Id;
        var start = CurrentMonthRange.Get().Start;
        await SeedCategorizedTransactionAsync(factory, userId, account.Id, start, target);
        await SeedCategorizedTransactionAsync(factory, userId, account.Id, start.AddDays(-1), target);
        await SeedCategorizedTransactionAsync(factory, userId, account.Id, start, other);

        var result = await (await client.GetAsync($"/api/transactions?categoryId={target}&currentMonth=true&take=100")).Content
            .ReadFromJsonAsync<TransactionListResponseDto>(JsonOptions);

        var item = Assert.Single(result!.Items);
        Assert.Equal(target, item.CategoryId);
        Assert.Equal(start, item.Date);
        Assert.False(result.HasMore);
    }

    [Fact]
    public async Task Post_WithoutAuthCookie_ReturnsUnauthorized()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/transactions/",
            new TransactionWriteRequest(new DateOnly(2026, 1, 1), "desc", 10m, Guid.NewGuid(), Guid.NewGuid(), false));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_WithoutValidAntiforgeryToken_ReturnsBadRequest()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var account = await CreateAccountAsync(client, "mBank", "111");
        var categoryId = await GetFirstCategoryIdAsync(client);

        var response = await client.PostAsJsonAsync(
            "/api/transactions/",
            new TransactionWriteRequest(new DateOnly(2026, 1, 1), "desc", 10m, account.Id, categoryId, false));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_CreatesTransaction_WithNullImportBatchIdAndMatchingHash()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var account = await CreateAccountAsync(client, "mBank", "111");
        var categoryId = await GetFirstCategoryIdAsync(client);
        var userId = await GetUserIdAsync(factory);
        var request = new TransactionWriteRequest(new DateOnly(2026, 1, 1), "Groceries run", 42.50m, account.Id, categoryId, false);

        var response = await PostTransactionAsync(client, request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<TransactionDetailDto>(JsonOptions);
        Assert.NotNull(dto);
        Assert.Equal(account.Id, dto!.AccountId);
        Assert.Equal(categoryId, dto.CategoryId);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Transactions.FirstAsync(t => t.Id == dto.Id);
        Assert.Null(stored.ImportBatchId);
        var expectedHash = DedupHash.ComputeHash(userId, request.Date, request.Amount, request.Description, request.AccountId);
        Assert.Equal(expectedHash, stored.Hash);
    }

    [Fact]
    public async Task Post_WithoutForce_AgainstExistingHash_ReturnsConflictWithExistingSnapshot()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var account = await CreateAccountAsync(client, "mBank", "111");
        var categoryId = await GetFirstCategoryIdAsync(client);
        var request = new TransactionWriteRequest(new DateOnly(2026, 1, 1), "Groceries run", 42.50m, account.Id, categoryId, false);

        var first = await PostTransactionAsync(client, request);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await PostTransactionAsync(client, request);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var body = await second.Content.ReadFromJsonAsync<DuplicateTransactionResponse>(JsonOptions);
        Assert.NotNull(body);
        Assert.Equal(request.Date, body!.ExistingTransaction.Date);
        Assert.Equal(request.Description, body.ExistingTransaction.Description);
        Assert.Equal(request.Amount, body.ExistingTransaction.Amount);
    }

    [Fact]
    public async Task Post_WithForce_AgainstExistingHash_InsertsAnyway()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var account = await CreateAccountAsync(client, "mBank", "111");
        var categoryId = await GetFirstCategoryIdAsync(client);
        var request = new TransactionWriteRequest(new DateOnly(2026, 1, 1), "Groceries run", 42.50m, account.Id, categoryId, false);

        var first = await PostTransactionAsync(client, request);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var forced = request with { Force = true };
        var second = await PostTransactionAsync(client, forced);

        Assert.Equal(HttpStatusCode.Created, second.StatusCode);

        var list = await (await client.GetAsync("/api/transactions?skip=0&take=100")).Content
            .ReadFromJsonAsync<TransactionListResponseDto>(JsonOptions);
        Assert.Equal(2, list!.Items.Count);
    }

    [Fact]
    public async Task Post_WithAccountNotOwnedByCaller_ReturnsBadRequest()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var categoryId = await GetFirstCategoryIdAsync(client);
        var otherAccount = await InsertAccountForOtherUserAsync(factory, "mBank", "999");

        var response = await PostTransactionAsync(
            client,
            new TransactionWriteRequest(new DateOnly(2026, 1, 1), "desc", 10m, otherAccount.Id, categoryId, false));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_WithUnknownCategoryId_ReturnsBadRequest()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var account = await CreateAccountAsync(client, "mBank", "111");

        var response = await PostTransactionAsync(
            client,
            new TransactionWriteRequest(new DateOnly(2026, 1, 1), "desc", 10m, account.Id, Guid.NewGuid(), false));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_WithoutAuthCookie_ReturnsUnauthorized()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/api/transactions/{Guid.NewGuid()}",
            new TransactionWriteRequest(new DateOnly(2026, 1, 1), "desc", 10m, Guid.NewGuid(), Guid.NewGuid(), false));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_WithoutValidAntiforgeryToken_ReturnsBadRequest()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var account = await CreateAccountAsync(client, "mBank", "111");
        var categoryId = await GetFirstCategoryIdAsync(client);
        var userId = await GetUserIdAsync(factory);
        var transaction = await InsertTransactionAsync(factory, userId, account.Id, new DateOnly(2026, 1, 1), 10m);

        var response = await client.PutAsJsonAsync(
            $"/api/transactions/{transaction.Id}",
            new TransactionWriteRequest(new DateOnly(2026, 1, 1), "desc", 10m, account.Id, categoryId, false));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_RecomputesHash_LeavesImportBatchIdUnchanged()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var account = await CreateAccountAsync(client, "mBank", "111");
        var categoryId = await GetFirstCategoryIdAsync(client);
        var userId = await GetUserIdAsync(factory);
        var importBatchId = Guid.NewGuid();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ImportBatches.Add(new ImportBatch
            {
                Id = importBatchId,
                UserId = userId,
                AccountId = account.Id,
                ImportedAtUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var seeded = await InsertTransactionAsync(
            factory, userId, account.Id, new DateOnly(2026, 1, 1), 10m, "original", importBatchId, "original-hash");

        var updateRequest = new TransactionWriteRequest(new DateOnly(2026, 2, 2), "updated desc", 55m, account.Id, categoryId, false);
        var response = await PutTransactionAsync(client, seeded.Id, updateRequest);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var verifyScope = factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var updated = await verifyDb.Transactions.FirstAsync(t => t.Id == seeded.Id);
        Assert.Equal(importBatchId, updated.ImportBatchId);
        var expectedHash = DedupHash.ComputeHash(userId, updateRequest.Date, updateRequest.Amount, updateRequest.Description, updateRequest.AccountId);
        Assert.Equal(expectedHash, updated.Hash);
    }

    [Fact]
    public async Task Put_WithoutForce_AgainstAnotherRowsHash_ReturnsConflict()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var account = await CreateAccountAsync(client, "mBank", "111");
        var categoryId = await GetFirstCategoryIdAsync(client);
        var otherRequest = new TransactionWriteRequest(new DateOnly(2026, 1, 1), "existing", 20m, account.Id, categoryId, false);
        var otherCreated = await PostTransactionAsync(client, otherRequest);
        Assert.Equal(HttpStatusCode.Created, otherCreated.StatusCode);

        var userId = await GetUserIdAsync(factory);
        var toEdit = await InsertTransactionAsync(factory, userId, account.Id, new DateOnly(2026, 3, 3), 99m, "to edit");

        var response = await PutTransactionAsync(client, toEdit.Id, otherRequest);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Put_SameRowUnchanged_WithoutForce_DoesNotConflictWithItself()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var account = await CreateAccountAsync(client, "mBank", "111");
        var categoryId = await GetFirstCategoryIdAsync(client);
        var request = new TransactionWriteRequest(new DateOnly(2026, 1, 1), "desc", 20m, account.Id, categoryId, false);

        var created = await PostTransactionAsync(client, request);
        var dto = await created.Content.ReadFromJsonAsync<TransactionDetailDto>(JsonOptions);

        var response = await PutTransactionAsync(client, dto!.Id, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Put_ForTransactionNotOwnedByCaller_ReturnsNotFound()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var account = await CreateAccountAsync(client, "mBank", "111");
        var categoryId = await GetFirstCategoryIdAsync(client);
        var otherAccount = await InsertAccountForOtherUserAsync(factory, "mBank", "222");
        var otherTransaction = await InsertTransactionAsync(factory, otherAccount.UserId, otherAccount.Id, new DateOnly(2026, 1, 1), 5m);

        var response = await PutTransactionAsync(
            client, otherTransaction.Id, new TransactionWriteRequest(new DateOnly(2026, 1, 1), "desc", 5m, account.Id, categoryId, false));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_WithAccountNotOwnedByCaller_ReturnsBadRequest()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var account = await CreateAccountAsync(client, "mBank", "111");
        var categoryId = await GetFirstCategoryIdAsync(client);
        var userId = await GetUserIdAsync(factory);
        var transaction = await InsertTransactionAsync(factory, userId, account.Id, new DateOnly(2026, 1, 1), 10m);
        var otherAccount = await InsertAccountForOtherUserAsync(factory, "mBank", "999");

        var response = await PutTransactionAsync(
            client, transaction.Id, new TransactionWriteRequest(new DateOnly(2026, 1, 1), "desc", 10m, otherAccount.Id, categoryId, false));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_WithUnknownCategoryId_ReturnsBadRequest()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var account = await CreateAccountAsync(client, "mBank", "111");
        var userId = await GetUserIdAsync(factory);
        var transaction = await InsertTransactionAsync(factory, userId, account.Id, new DateOnly(2026, 1, 1), 10m);

        var response = await PutTransactionAsync(
            client, transaction.Id, new TransactionWriteRequest(new DateOnly(2026, 1, 1), "desc", 10m, account.Id, Guid.NewGuid(), false));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Delete_WithoutAuthCookie_ReturnsUnauthorized()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();

        var response = await client.DeleteAsync($"/api/transactions/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Delete_WithoutValidAntiforgeryToken_ReturnsBadRequest()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var account = await CreateAccountAsync(client, "mBank", "111");
        var userId = await GetUserIdAsync(factory);
        var transaction = await InsertTransactionAsync(factory, userId, account.Id, new DateOnly(2026, 1, 1), 10m);

        var response = await client.DeleteAsync($"/api/transactions/{transaction.Id}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Delete_RemovesTransaction_RegardlessOfImportBatchId()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var account = await CreateAccountAsync(client, "mBank", "111");
        var userId = await GetUserIdAsync(factory);
        var importBatchId = Guid.NewGuid();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ImportBatches.Add(new ImportBatch
            {
                Id = importBatchId,
                UserId = userId,
                AccountId = account.Id,
                ImportedAtUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var imported = await InsertTransactionAsync(factory, userId, account.Id, new DateOnly(2026, 1, 1), 10m, "imported", importBatchId);
        var manual = await InsertTransactionAsync(factory, userId, account.Id, new DateOnly(2026, 1, 2), 20m, "manual");

        var deleteImported = await DeleteTransactionAsync(client, imported.Id);
        var deleteManual = await DeleteTransactionAsync(client, manual.Id);

        Assert.Equal(HttpStatusCode.NoContent, deleteImported.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, deleteManual.StatusCode);

        var list = await (await client.GetAsync("/api/transactions?skip=0&take=100")).Content
            .ReadFromJsonAsync<TransactionListResponseDto>(JsonOptions);
        Assert.Empty(list!.Items);
    }

    [Fact]
    public async Task Delete_ForTransactionNotOwnedByCaller_ReturnsNotFound()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var otherAccount = await InsertAccountForOtherUserAsync(factory, "mBank", "999");
        var otherTransaction = await InsertTransactionAsync(factory, otherAccount.UserId, otherAccount.Id, new DateOnly(2026, 1, 1), 5m);

        var response = await DeleteTransactionAsync(client, otherTransaction.Id);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
