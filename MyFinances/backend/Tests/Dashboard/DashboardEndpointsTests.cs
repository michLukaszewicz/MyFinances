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
using MyFinances.Api.Tests.Auth;
using MyFinances.Api.Tests.Support;

namespace MyFinances.Api.Tests.Dashboard;

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
        // Arrange
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/dashboard/category-spend");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CategorySpend_SumsCurrentMonthSpendPerCategoryAsPositiveAmount()
    {
        // Arrange
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

        // Act
        var result = await GetSpendAsync(client);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal(GroceriesId, result[0].CategoryId);
        Assert.Equal(15m, result[0].Amount);
        Assert.Equal(TransportId, result[1].CategoryId);
        Assert.Equal(20m, result[1].Amount);
    }

    [Fact]
    public async Task CategorySpend_ExcludesUncategorizedTransactions()
    {
        // Arrange
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var userId = await GetUserIdAsync(factory);
        var accountId = await SeedAccountAsync(factory, userId, "111");
        await SeedAsync(factory, userId, accountId, CurrentMonthRange.Get().Start, -50m, null);

        // Act
        var result = await GetSpendAsync(client);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task CategorySpend_ExcludesInternalTransfers()
    {
        // Arrange
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var userId = await GetUserIdAsync(factory);
        var accountId = await SeedAccountAsync(factory, userId, "111");
        var start = CurrentMonthRange.Get().Start;
        await SeedAsync(factory, userId, accountId, start, -300m, GroceriesId, isInternalTransfer: true);
        await SeedAsync(factory, userId, accountId, start, -5m, GroceriesId);

        // Act
        var result = await GetSpendAsync(client);

        // Assert
        var item = Assert.Single(result);
        Assert.Equal(5m, item.Amount);
    }

    [Fact]
    public async Task CategorySpend_ExcludesTransactionsOutsideCurrentMonth()
    {
        // Arrange
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var userId = await GetUserIdAsync(factory);
        var accountId = await SeedAccountAsync(factory, userId, "111");
        var (start, end) = CurrentMonthRange.Get();
        await SeedAsync(factory, userId, accountId, start.AddDays(-1), -40m, GroceriesId);
        await SeedAsync(factory, userId, accountId, end.AddDays(1), -60m, GroceriesId);
        await SeedAsync(factory, userId, accountId, end, -7m, GroceriesId);

        // Act
        var result = await GetSpendAsync(client);

        // Assert
        var item = Assert.Single(result);
        Assert.Equal(7m, item.Amount);
    }

    [Fact]
    public async Task CategorySpend_OmitsCategoriesWithNoCurrentMonthSpend()
    {
        // Arrange
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var userId = await GetUserIdAsync(factory);
        var accountId = await SeedAccountAsync(factory, userId, "111");
        var (start, _) = CurrentMonthRange.Get();
        await SeedAsync(factory, userId, accountId, start.AddDays(-1), -40m, DiningId);
        await SeedAsync(factory, userId, accountId, start, -8m, GroceriesId);

        // Act
        var result = await GetSpendAsync(client);

        // Assert
        var item = Assert.Single(result);
        Assert.Equal(GroceriesId, item.CategoryId);
        Assert.DoesNotContain(result, r => r.CategoryId == DiningId);
    }

    [Fact]
    public async Task CategorySpend_OnlyIncludesCallingUsersTransactions()
    {
        // Arrange
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var userId = await GetUserIdAsync(factory);
        var accountId = await SeedAccountAsync(factory, userId, "111");
        var otherUserId = Guid.NewGuid();
        var otherAccountId = await SeedAccountAsync(factory, otherUserId, "222");
        var start = CurrentMonthRange.Get().Start;
        await SeedAsync(factory, userId, accountId, start, -12m, GroceriesId);
        await SeedAsync(factory, otherUserId, otherAccountId, start, -999m, GroceriesId);

        // Act
        var result = await GetSpendAsync(client);

        // Assert
        var item = Assert.Single(result);
        Assert.Equal(12m, item.Amount);
    }

    [Fact]
    public async Task CategoryIncome_SumsOnlyPositiveNonTransferCategorizedCurrentMonthRows()
    {
        // Arrange
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

        // Act
        var response = await client.GetAsync("/api/dashboard/category-income");
        var result = await response.Content.ReadFromJsonAsync<List<CategorySpendDto>>(JsonOptions);

        // Assert
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
        // Arrange
        using var baseFactory = new AuthApiFactory();
        var factory = WithClock(baseFactory, March10);
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var userId = await GetUserIdAsync(factory);
        var accountId = await SeedAccountAsync(factory, userId, "111");
        await SeedAsync(factory, userId, accountId, new DateOnly(2099, 2, 5), -100m, GroceriesId);
        await SeedAsync(factory, userId, accountId, new DateOnly(2099, 3, 3), -300m, GroceriesId);

        // Act
        var item = Assert.Single(await GetSignalAsync(client));

        // Assert
        Assert.Equal(300m, item.Amount);
        Assert.Equal(100m, item.AverageToDate);
        Assert.Equal("above", item.Deviation);
    }

    [Fact]
    public async Task CategorySpend_FutureDatedCurrentMonthSpend_CountsInAmountButNotInComparison()
    {
        // Arrange
        using var baseFactory = new AuthApiFactory();
        var factory = WithClock(baseFactory, March10);
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var userId = await GetUserIdAsync(factory);
        var accountId = await SeedAccountAsync(factory, userId, "111");
        await SeedAsync(factory, userId, accountId, new DateOnly(2099, 2, 5), -100m, GroceriesId);
        await SeedAsync(factory, userId, accountId, new DateOnly(2099, 3, 3), -100m, GroceriesId);
        await SeedAsync(factory, userId, accountId, new DateOnly(2099, 3, 20), -500m, GroceriesId);

        // Act
        var item = Assert.Single(await GetSignalAsync(client));

        // Assert
        Assert.Equal(600m, item.Amount);
        Assert.Equal("inLine", item.Deviation);
    }

    [Fact]
    public async Task CategorySpend_NoPriorHistory_HasNullSignal()
    {
        // Arrange
        using var baseFactory = new AuthApiFactory();
        var factory = WithClock(baseFactory, March10);
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var userId = await GetUserIdAsync(factory);
        var accountId = await SeedAccountAsync(factory, userId, "111");
        await SeedAsync(factory, userId, accountId, new DateOnly(2099, 3, 3), -50m, GroceriesId);

        // Act
        var item = Assert.Single(await GetSignalAsync(client));

        // Assert
        Assert.Null(item.AverageToDate);
        Assert.Null(item.Deviation);
    }

    [Fact]
    public async Task CategorySpend_HistoryAlone_DoesNotAddARow()
    {
        // Arrange
        using var baseFactory = new AuthApiFactory();
        var factory = WithClock(baseFactory, March10);
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var userId = await GetUserIdAsync(factory);
        var accountId = await SeedAccountAsync(factory, userId, "111");
        await SeedAsync(factory, userId, accountId, new DateOnly(2099, 2, 5), -100m, GroceriesId);

        // Act & Assert
        Assert.Empty(await GetSignalAsync(client));
    }

    [Fact]
    public async Task CategorySpend_HistoryExcludesInternalTransfers()
    {
        // Arrange
        using var baseFactory = new AuthApiFactory();
        var factory = WithClock(baseFactory, March10);
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var userId = await GetUserIdAsync(factory);
        var accountId = await SeedAccountAsync(factory, userId, "111");
        await SeedAsync(factory, userId, accountId, new DateOnly(2099, 2, 5), -1000m, GroceriesId, isInternalTransfer: true);
        await SeedAsync(factory, userId, accountId, new DateOnly(2099, 3, 3), -10m, GroceriesId);

        // Act
        var item = Assert.Single(await GetSignalAsync(client));

        // Assert
        Assert.Null(item.Deviation);
    }

    [Fact]
    public async Task CategorySpend_HistoryIsolatedPerUser()
    {
        // Arrange
        using var baseFactory = new AuthApiFactory();
        var factory = WithClock(baseFactory, March10);
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var userId = await GetUserIdAsync(factory);
        var accountId = await SeedAccountAsync(factory, userId, "111");
        var otherUserId = Guid.NewGuid();
        var otherAccountId = await SeedAccountAsync(factory, otherUserId, "222");
        await SeedAsync(factory, otherUserId, otherAccountId, new DateOnly(2099, 2, 5), -1000m, GroceriesId);
        await SeedAsync(factory, userId, accountId, new DateOnly(2099, 3, 3), -10m, GroceriesId);

        // Act
        var item = Assert.Single(await GetSignalAsync(client));

        // Assert
        Assert.Null(item.Deviation);
    }

    [Fact]
    public async Task CategorySpend_InJanuary_HistoryIncludesPreviousDecember()
    {
        // Arrange
        using var baseFactory = new AuthApiFactory();
        var factory = WithClock(baseFactory, new DateTimeOffset(2100, 1, 10, 12, 0, 0, TimeSpan.Zero));
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var userId = await GetUserIdAsync(factory);
        var accountId = await SeedAccountAsync(factory, userId, "111");
        await SeedAsync(factory, userId, accountId, new DateOnly(2099, 12, 5), -100m, GroceriesId);
        await SeedAsync(factory, userId, accountId, new DateOnly(2100, 1, 3), -300m, GroceriesId);

        // Act
        var item = Assert.Single(await GetSignalAsync(client));

        // Assert
        Assert.Equal(100m, item.AverageToDate);
        Assert.Equal("above", item.Deviation);
    }

    [Fact]
    public async Task CategoryIncome_ResponseHasNoSignalFields()
    {
        // Arrange
        using var baseFactory = new AuthApiFactory();
        var factory = WithClock(baseFactory, March10);
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var userId = await GetUserIdAsync(factory);
        var accountId = await SeedAccountAsync(factory, userId, "111");
        await SeedAsync(factory, userId, accountId, new DateOnly(2099, 2, 5), 100m, GroceriesId);
        await SeedAsync(factory, userId, accountId, new DateOnly(2099, 3, 3), 300m, GroceriesId);

        // Act
        var json = await client.GetStringAsync("/api/dashboard/category-income");

        // Assert
        using var doc = JsonDocument.Parse(json);
        var element = doc.RootElement[0];
        Assert.Equal(300m, element.GetProperty("amount").GetDecimal());
        Assert.False(element.TryGetProperty("averageToDate", out _));
        Assert.False(element.TryGetProperty("deviation", out _));
    }

    private static async Task<HttpResponseMessage> GetPeriodAsync(HttpClient client, string kind, string from, string to) =>
        await client.GetAsync($"/api/dashboard/category-{kind}?from={from}&to={to}");

    [Fact]
    public async Task CategorySpend_WithPeriod_IncludesBothEndsAndExcludesOutside()
    {
        // Arrange
        using var baseFactory = new AuthApiFactory();
        var factory = WithClock(baseFactory, March10);
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var userId = await GetUserIdAsync(factory);
        var accountId = await SeedAccountAsync(factory, userId, "111");
        await SeedAsync(factory, userId, accountId, new DateOnly(2099, 2, 9), -1000m, GroceriesId);
        await SeedAsync(factory, userId, accountId, new DateOnly(2099, 2, 10), -1m, GroceriesId);
        await SeedAsync(factory, userId, accountId, new DateOnly(2099, 2, 20), -2m, GroceriesId);
        await SeedAsync(factory, userId, accountId, new DateOnly(2099, 2, 25), -4m, GroceriesId);
        await SeedAsync(factory, userId, accountId, new DateOnly(2099, 2, 26), -1000m, GroceriesId);

        // Act
        var response = await GetPeriodAsync(client, "spend", "2099-02-10", "2099-02-25");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = Assert.Single((await response.Content.ReadFromJsonAsync<List<CategorySpendSignalDto>>(JsonOptions))!);
        Assert.Equal(7m, item.Amount);
    }

    [Fact]
    public async Task CategoryIncome_WithPeriod_FiltersByRange()
    {
        // Arrange
        using var baseFactory = new AuthApiFactory();
        var factory = WithClock(baseFactory, March10);
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var userId = await GetUserIdAsync(factory);
        var accountId = await SeedAccountAsync(factory, userId, "111");
        await SeedAsync(factory, userId, accountId, new DateOnly(2099, 2, 10), 40m, GroceriesId);
        await SeedAsync(factory, userId, accountId, new DateOnly(2099, 3, 2), 900m, GroceriesId);

        // Act
        var response = await GetPeriodAsync(client, "income", "2099-02-01", "2099-02-28");

        // Assert
        var item = Assert.Single((await response.Content.ReadFromJsonAsync<List<CategorySpendDto>>(JsonOptions))!);
        Assert.Equal(40m, item.Amount);
    }

    [Fact]
    public async Task CategorySpend_WithoutPeriod_EqualsExplicitCurrentMonth()
    {
        // Arrange
        using var baseFactory = new AuthApiFactory();
        var factory = WithClock(baseFactory, March10);
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var userId = await GetUserIdAsync(factory);
        var accountId = await SeedAccountAsync(factory, userId, "111");
        await SeedAsync(factory, userId, accountId, new DateOnly(2099, 2, 28), -100m, GroceriesId);
        await SeedAsync(factory, userId, accountId, new DateOnly(2099, 3, 3), -30m, GroceriesId);
        await SeedAsync(factory, userId, accountId, new DateOnly(2099, 3, 25), -12m, GroceriesId);

        // Act
        var implicitMonth = await client.GetStringAsync("/api/dashboard/category-spend");
        var explicitMonth = await client.GetStringAsync("/api/dashboard/category-spend?from=2099-03-01&to=2099-03-31");

        // Assert
        Assert.Equal(implicitMonth, explicitMonth);
        Assert.Contains("42", implicitMonth);
    }

    [Fact]
    public async Task CategorySpend_WithInvalidPeriod_ReturnsBadRequest()
    {
        // Arrange
        using var baseFactory = new AuthApiFactory();
        var factory = WithClock(baseFactory, March10);
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);

        // Act & Assert
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/dashboard/category-spend?from=2099-03-01")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/dashboard/category-spend?to=2099-03-01")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await GetPeriodAsync(client, "spend", "2099-03-05", "2099-03-01")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await GetPeriodAsync(client, "spend", "2099-03-11", "2099-03-20")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await GetPeriodAsync(client, "income", "2099-02-01", "2099-03-20")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await GetPeriodAsync(client, "income", "2099-03-05", "2099-03-01")).StatusCode);
    }

    [Fact]
    public async Task CategorySpend_CurrentCalendarMonthWithFutureEnd_CountsFutureDatedRow()
    {
        // Arrange
        using var baseFactory = new AuthApiFactory();
        var factory = WithClock(baseFactory, March10);
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var userId = await GetUserIdAsync(factory);
        var accountId = await SeedAccountAsync(factory, userId, "111");
        await SeedAsync(factory, userId, accountId, new DateOnly(2099, 3, 28), -9m, GroceriesId);

        // Act
        var response = await GetPeriodAsync(client, "spend", "2099-03-01", "2099-03-31");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = Assert.Single((await response.Content.ReadFromJsonAsync<List<CategorySpendSignalDto>>(JsonOptions))!);
        Assert.Equal(9m, item.Amount);
    }

    [Fact]
    public async Task CategorySpend_PeriodAcrossYearBoundary_SumsBothYears()
    {
        // Arrange
        using var baseFactory = new AuthApiFactory();
        var factory = WithClock(baseFactory, new DateTimeOffset(2100, 1, 10, 12, 0, 0, TimeSpan.Zero));
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var userId = await GetUserIdAsync(factory);
        var accountId = await SeedAccountAsync(factory, userId, "111");
        await SeedAsync(factory, userId, accountId, new DateOnly(2099, 12, 20), -5m, GroceriesId);
        await SeedAsync(factory, userId, accountId, new DateOnly(2100, 1, 5), -6m, GroceriesId);
        await SeedAsync(factory, userId, accountId, new DateOnly(2099, 12, 19), -100m, GroceriesId);

        // Act
        var response = await GetPeriodAsync(client, "spend", "2099-12-20", "2100-01-05");

        // Assert
        var item = Assert.Single((await response.Content.ReadFromJsonAsync<List<CategorySpendSignalDto>>(JsonOptions))!);
        Assert.Equal(11m, item.Amount);
    }

    [Fact]
    public async Task CategorySpend_PastMonth_ComparesFullMonthAgainstEarlierMonths()
    {
        // Arrange
        using var baseFactory = new AuthApiFactory();
        var factory = WithClock(baseFactory, March10);
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var userId = await GetUserIdAsync(factory);
        var accountId = await SeedAccountAsync(factory, userId, "111");
        await SeedAsync(factory, userId, accountId, new DateOnly(2099, 1, 25), -100m, GroceriesId);
        await SeedAsync(factory, userId, accountId, new DateOnly(2099, 2, 27), -300m, GroceriesId);

        // Act
        var response = await GetPeriodAsync(client, "spend", "2099-02-01", "2099-02-28");

        // Assert
        var item = Assert.Single((await response.Content.ReadFromJsonAsync<List<CategorySpendSignalDto>>(JsonOptions))!);
        Assert.Equal(300m, item.Amount);
        Assert.Equal(100m, item.AverageToDate);
        Assert.Equal("above", item.Deviation);
    }

    [Fact]
    public async Task CategorySpend_Last30Days_ComparesAgainstPrecedingThirtyDayWindows()
    {
        // Arrange
        using var baseFactory = new AuthApiFactory();
        var factory = WithClock(baseFactory, March10);
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var userId = await GetUserIdAsync(factory);
        var accountId = await SeedAccountAsync(factory, userId, "111");
        // Window is 2099-02-09 .. 2099-03-10; one earlier window starting 2099-01-10.
        await SeedAsync(factory, userId, accountId, new DateOnly(2099, 1, 10), -100m, GroceriesId);
        await SeedAsync(factory, userId, accountId, new DateOnly(2099, 2, 9), -150m, GroceriesId);
        await SeedAsync(factory, userId, accountId, new DateOnly(2099, 3, 10), -150m, GroceriesId);

        // Act
        var response = await GetPeriodAsync(client, "spend", "2099-02-09", "2099-03-10");

        // Assert
        var item = Assert.Single((await response.Content.ReadFromJsonAsync<List<CategorySpendSignalDto>>(JsonOptions))!);
        Assert.Equal(300m, item.Amount);
        Assert.Equal(100m, item.AverageToDate);
        Assert.Equal("above", item.Deviation);
    }

    [Fact]
    public async Task CategorySpend_CustomRangeWithoutPriorSpend_HasNullSignal()
    {
        // Arrange
        using var baseFactory = new AuthApiFactory();
        var factory = WithClock(baseFactory, March10);
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var userId = await GetUserIdAsync(factory);
        var accountId = await SeedAccountAsync(factory, userId, "111");
        await SeedAsync(factory, userId, accountId, new DateOnly(2099, 2, 12), -50m, GroceriesId);

        // Act
        var response = await GetPeriodAsync(client, "spend", "2099-02-10", "2099-02-14");

        // Assert
        var item = Assert.Single((await response.Content.ReadFromJsonAsync<List<CategorySpendSignalDto>>(JsonOptions))!);
        Assert.Null(item.Deviation);
    }

    [Fact]
    public async Task CategorySpend_PeriodIsolatedPerUser()
    {
        // Arrange
        using var baseFactory = new AuthApiFactory();
        var factory = WithClock(baseFactory, March10);
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var userId = await GetUserIdAsync(factory);
        var accountId = await SeedAccountAsync(factory, userId, "111");
        var otherUserId = Guid.NewGuid();
        var otherAccountId = await SeedAccountAsync(factory, otherUserId, "222");
        await SeedAsync(factory, userId, accountId, new DateOnly(2099, 2, 10), -3m, GroceriesId);
        await SeedAsync(factory, otherUserId, otherAccountId, new DateOnly(2099, 2, 10), -999m, GroceriesId);

        // Act
        var response = await GetPeriodAsync(client, "spend", "2099-02-01", "2099-02-28");

        // Assert
        var item = Assert.Single((await response.Content.ReadFromJsonAsync<List<CategorySpendSignalDto>>(JsonOptions))!);
        Assert.Equal(3m, item.Amount);
    }

    [Fact]
    public async Task CategoryIncome_WithoutAuthCookie_ReturnsUnauthorized()
    {
        // Arrange
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/dashboard/category-income");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
