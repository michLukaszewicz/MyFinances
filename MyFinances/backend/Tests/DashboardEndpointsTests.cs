using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MyFinances.Api;
using MyFinances.Api.Dashboard;
using MyFinances.Api.Transactions;
using Xunit;

namespace MyFinances.Api.Tests;

// Coverage for GET /api/dashboard/category-spend. Seeds directly via AppDbContext on a single
// account per user, so TransferDetectionService (which needs two different accounts) never
// auto-flags seeded rows; internal-transfer rows are seeded with an explicit manual flag.
public class DashboardEndpointsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly Guid GroceriesId = new("00000000-0000-0000-0000-000000000001");
    private static readonly Guid DiningId = new("00000000-0000-0000-0000-000000000002");
    private static readonly Guid TransportId = new("00000000-0000-0000-0000-000000000003");

    private static async Task<Guid> GetUserIdAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = await userManager.FindByEmailAsync(AuthApiFactory.AllowedEmail);
        return user!.Id;
    }

    private static async Task<Guid> SeedAccountAsync(WebApplicationFactory<Program> factory, Guid userId, string number)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var account = new Account { Id = Guid.NewGuid(), UserId = userId, BankName = "mBank", AccountNumber = number };
        db.Accounts.Add(account);
        await db.SaveChangesAsync();
        return account.Id;
    }

    private static async Task SeedAsync(
        WebApplicationFactory<Program> factory,
        Guid userId,
        Guid accountId,
        DateOnly date,
        decimal amount,
        Guid? categoryId,
        bool isInternalTransfer = false)
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
            Amount = amount,
            Hash = Guid.NewGuid().ToString(),
            CategoryId = categoryId,
            IsInternalTransfer = isInternalTransfer,
            TransferFlagManuallySet = isInternalTransfer,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<List<CategorySpendDto>> GetSpendAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/dashboard/category-spend");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<List<CategorySpendDto>>(JsonOptions))!;
    }

    [Fact]
    public async Task CategorySpend_WithoutAuthCookie_ReturnsUnauthorized()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/dashboard/category-spend");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CategorySpend_SumsCurrentMonthSpendPerCategoryAsPositiveAmount()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var userId = await GetUserIdAsync(factory);
        var accountId = await SeedAccountAsync(factory, userId, "111");
        var start = CurrentMonthRange.Get().Start;
        await SeedAsync(factory, userId, accountId, start, -10.50m, GroceriesId);
        await SeedAsync(factory, userId, accountId, start, -4.50m, GroceriesId);
        await SeedAsync(factory, userId, accountId, start, -20m, TransportId);
        // Positive amounts (refunds/income) are not spend.
        await SeedAsync(factory, userId, accountId, start, 100m, GroceriesId);

        var result = await GetSpendAsync(client);

        Assert.Equal(2, result.Count);
        Assert.Equal(GroceriesId, result[0].CategoryId);
        Assert.Equal(15m, result[0].Amount);
        Assert.Equal(TransportId, result[1].CategoryId);
        Assert.Equal(20m, result[1].Amount);
    }

    [Fact]
    public async Task CategorySpend_ExcludesUncategorizedTransactions()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var userId = await GetUserIdAsync(factory);
        var accountId = await SeedAccountAsync(factory, userId, "111");
        await SeedAsync(factory, userId, accountId, CurrentMonthRange.Get().Start, -50m, null);

        var result = await GetSpendAsync(client);

        Assert.Empty(result);
    }

    [Fact]
    public async Task CategorySpend_ExcludesInternalTransfers()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var userId = await GetUserIdAsync(factory);
        var accountId = await SeedAccountAsync(factory, userId, "111");
        var start = CurrentMonthRange.Get().Start;
        await SeedAsync(factory, userId, accountId, start, -300m, GroceriesId, isInternalTransfer: true);
        await SeedAsync(factory, userId, accountId, start, -5m, GroceriesId);

        var result = await GetSpendAsync(client);

        var item = Assert.Single(result);
        Assert.Equal(5m, item.Amount);
    }

    [Fact]
    public async Task CategorySpend_ExcludesTransactionsOutsideCurrentMonth()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var userId = await GetUserIdAsync(factory);
        var accountId = await SeedAccountAsync(factory, userId, "111");
        var (start, end) = CurrentMonthRange.Get();
        await SeedAsync(factory, userId, accountId, start.AddDays(-1), -40m, GroceriesId);
        await SeedAsync(factory, userId, accountId, end.AddDays(1), -60m, GroceriesId);
        await SeedAsync(factory, userId, accountId, end, -7m, GroceriesId);

        var result = await GetSpendAsync(client);

        var item = Assert.Single(result);
        Assert.Equal(7m, item.Amount);
    }

    [Fact]
    public async Task CategorySpend_OmitsCategoriesWithNoCurrentMonthSpend()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var userId = await GetUserIdAsync(factory);
        var accountId = await SeedAccountAsync(factory, userId, "111");
        var (start, _) = CurrentMonthRange.Get();
        await SeedAsync(factory, userId, accountId, start.AddDays(-1), -40m, DiningId);
        await SeedAsync(factory, userId, accountId, start, -8m, GroceriesId);

        var result = await GetSpendAsync(client);

        var item = Assert.Single(result);
        Assert.Equal(GroceriesId, item.CategoryId);
        Assert.DoesNotContain(result, r => r.CategoryId == DiningId);
    }

    [Fact]
    public async Task CategorySpend_OnlyIncludesCallingUsersTransactions()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var userId = await GetUserIdAsync(factory);
        var accountId = await SeedAccountAsync(factory, userId, "111");
        var otherUserId = Guid.NewGuid();
        var otherAccountId = await SeedAccountAsync(factory, otherUserId, "222");
        var start = CurrentMonthRange.Get().Start;
        await SeedAsync(factory, userId, accountId, start, -12m, GroceriesId);
        await SeedAsync(factory, otherUserId, otherAccountId, start, -999m, GroceriesId);

        var result = await GetSpendAsync(client);

        var item = Assert.Single(result);
        Assert.Equal(12m, item.Amount);
    }

    [Fact]
    public async Task CategoryIncome_SumsOnlyPositiveNonTransferCategorizedCurrentMonthRows()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var userId = await GetUserIdAsync(factory);
        var accountId = await SeedAccountAsync(factory, userId, "111");
        var (start, end) = CurrentMonthRange.Get();
        await SeedAsync(factory, userId, accountId, start, 100m, GroceriesId);
        await SeedAsync(factory, userId, accountId, end, 50m, GroceriesId);
        await SeedAsync(factory, userId, accountId, start, -30m, GroceriesId);
        await SeedAsync(factory, userId, accountId, start, 70m, null);
        await SeedAsync(factory, userId, accountId, start, 80m, DiningId, isInternalTransfer: true);
        await SeedAsync(factory, userId, accountId, start.AddDays(-1), 90m, TransportId);

        var response = await client.GetAsync("/api/dashboard/category-income");
        var result = await response.Content.ReadFromJsonAsync<List<CategorySpendDto>>(JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = Assert.Single(result!);
        Assert.Equal(GroceriesId, item.CategoryId);
        Assert.Equal(150m, item.Amount);
    }

    // Fixed "today" for signal tests: 10 Mar 2099 (noon UTC is 13:00 in Warsaw, same date). Far future on
    // purpose: the persistent auth cookie expires 30 days after the fake "now", and the test HttpClient
    // drops cookies already expired by the real clock.
    private static WebApplicationFactory<Program> WithClock(AuthApiFactory factory, DateTimeOffset utcNow) =>
        factory.WithWebHostBuilder(b => b.ConfigureServices(services =>
        {
            foreach (var d in services.Where(d => d.ServiceType == typeof(TimeProvider)).ToList())
            {
                services.Remove(d);
            }
            services.AddSingleton<TimeProvider>(new FixedTimeProvider(utcNow));
        }));

    private static readonly DateTimeOffset March10 = new(2099, 3, 10, 12, 0, 0, TimeSpan.Zero);

    private static async Task<List<CategorySpendSignalDto>> GetSignalAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/dashboard/category-spend");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<List<CategorySpendSignalDto>>(JsonOptions))!;
    }

    [Fact]
    public async Task CategorySpend_SpendAbovePriorPace_ReportsAboveWithAverage()
    {
        using var baseFactory = new AuthApiFactory();
        var factory = WithClock(baseFactory, March10);
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var userId = await GetUserIdAsync(factory);
        var accountId = await SeedAccountAsync(factory, userId, "111");
        await SeedAsync(factory, userId, accountId, new DateOnly(2099, 2, 5), -100m, GroceriesId);
        await SeedAsync(factory, userId, accountId, new DateOnly(2099, 3, 3), -300m, GroceriesId);

        var item = Assert.Single(await GetSignalAsync(client));

        Assert.Equal(300m, item.Amount);
        Assert.Equal(100m, item.AverageToDate);
        Assert.Equal("above", item.Deviation);
    }

    [Fact]
    public async Task CategorySpend_FutureDatedCurrentMonthSpend_CountsInAmountButNotInComparison()
    {
        using var baseFactory = new AuthApiFactory();
        var factory = WithClock(baseFactory, March10);
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var userId = await GetUserIdAsync(factory);
        var accountId = await SeedAccountAsync(factory, userId, "111");
        await SeedAsync(factory, userId, accountId, new DateOnly(2099, 2, 5), -100m, GroceriesId);
        await SeedAsync(factory, userId, accountId, new DateOnly(2099, 3, 3), -100m, GroceriesId);
        await SeedAsync(factory, userId, accountId, new DateOnly(2099, 3, 20), -500m, GroceriesId);

        var item = Assert.Single(await GetSignalAsync(client));

        Assert.Equal(600m, item.Amount);
        Assert.Equal("inLine", item.Deviation);
    }

    [Fact]
    public async Task CategorySpend_NoPriorHistory_HasNullSignal()
    {
        using var baseFactory = new AuthApiFactory();
        var factory = WithClock(baseFactory, March10);
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var userId = await GetUserIdAsync(factory);
        var accountId = await SeedAccountAsync(factory, userId, "111");
        await SeedAsync(factory, userId, accountId, new DateOnly(2099, 3, 3), -50m, GroceriesId);

        var item = Assert.Single(await GetSignalAsync(client));

        Assert.Null(item.AverageToDate);
        Assert.Null(item.Deviation);
    }

    [Fact]
    public async Task CategorySpend_HistoryAlone_DoesNotAddARow()
    {
        using var baseFactory = new AuthApiFactory();
        var factory = WithClock(baseFactory, March10);
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var userId = await GetUserIdAsync(factory);
        var accountId = await SeedAccountAsync(factory, userId, "111");
        await SeedAsync(factory, userId, accountId, new DateOnly(2099, 2, 5), -100m, GroceriesId);

        Assert.Empty(await GetSignalAsync(client));
    }

    [Fact]
    public async Task CategorySpend_HistoryExcludesInternalTransfersAndUncategorizedRows()
    {
        using var baseFactory = new AuthApiFactory();
        var factory = WithClock(baseFactory, March10);
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var userId = await GetUserIdAsync(factory);
        var accountId = await SeedAccountAsync(factory, userId, "111");
        await SeedAsync(factory, userId, accountId, new DateOnly(2099, 2, 5), -1000m, GroceriesId, isInternalTransfer: true);
        await SeedAsync(factory, userId, accountId, new DateOnly(2099, 2, 6), -1000m, null);
        await SeedAsync(factory, userId, accountId, new DateOnly(2099, 3, 3), -10m, GroceriesId);

        var item = Assert.Single(await GetSignalAsync(client));

        Assert.Null(item.Deviation);
    }

    [Fact]
    public async Task CategorySpend_HistoryIsolatedPerUser()
    {
        using var baseFactory = new AuthApiFactory();
        var factory = WithClock(baseFactory, March10);
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var userId = await GetUserIdAsync(factory);
        var accountId = await SeedAccountAsync(factory, userId, "111");
        var otherUserId = Guid.NewGuid();
        var otherAccountId = await SeedAccountAsync(factory, otherUserId, "222");
        await SeedAsync(factory, otherUserId, otherAccountId, new DateOnly(2099, 2, 5), -1000m, GroceriesId);
        await SeedAsync(factory, userId, accountId, new DateOnly(2099, 3, 3), -10m, GroceriesId);

        var item = Assert.Single(await GetSignalAsync(client));

        Assert.Null(item.Deviation);
    }

    [Fact]
    public async Task CategorySpend_InJanuary_HistoryIncludesPreviousDecember()
    {
        using var baseFactory = new AuthApiFactory();
        var factory = WithClock(baseFactory, new DateTimeOffset(2100, 1, 10, 12, 0, 0, TimeSpan.Zero));
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var userId = await GetUserIdAsync(factory);
        var accountId = await SeedAccountAsync(factory, userId, "111");
        await SeedAsync(factory, userId, accountId, new DateOnly(2099, 12, 5), -100m, GroceriesId);
        await SeedAsync(factory, userId, accountId, new DateOnly(2100, 1, 3), -300m, GroceriesId);

        var item = Assert.Single(await GetSignalAsync(client));

        Assert.Equal(100m, item.AverageToDate);
        Assert.Equal("above", item.Deviation);
    }

    [Fact]
    public async Task CategoryIncome_ResponseHasNoSignalFields()
    {
        using var baseFactory = new AuthApiFactory();
        var factory = WithClock(baseFactory, March10);
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var userId = await GetUserIdAsync(factory);
        var accountId = await SeedAccountAsync(factory, userId, "111");
        await SeedAsync(factory, userId, accountId, new DateOnly(2099, 2, 5), 100m, GroceriesId);
        await SeedAsync(factory, userId, accountId, new DateOnly(2099, 3, 3), 300m, GroceriesId);

        var json = await client.GetStringAsync("/api/dashboard/category-income");

        using var doc = JsonDocument.Parse(json);
        var element = doc.RootElement[0];
        Assert.Equal(300m, element.GetProperty("amount").GetDecimal());
        Assert.False(element.TryGetProperty("averageToDate", out _));
        Assert.False(element.TryGetProperty("deviation", out _));
    }

    [Fact]
    public async Task CategoryIncome_WithoutAuthCookie_ReturnsUnauthorized()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/dashboard/category-income");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
