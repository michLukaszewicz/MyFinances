using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MyFinances.Api.Dashboard;
using MyFinances.Api.Transactions;
using Xunit;

namespace MyFinances.Api.Tests;

// Coverage for GET /api/dashboard/category-trend. Dates sit in 2099/2100 with a fake clock because
// the auth cookie expires 30 days after "now" and the test client drops cookies expired by the real clock.
public class CategoryTrendEndpointsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly Guid GroceriesId = new("00000000-0000-0000-0000-000000000001");
    private static readonly Guid DiningId = new("00000000-0000-0000-0000-000000000002");

    // 2099-03-10 is a Tuesday; 2100-01-20 is well past the year boundary used by the week tests.
    private static readonly DateTimeOffset March10 = new(2099, 3, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Jan20 = new(2100, 1, 20, 12, 0, 0, TimeSpan.Zero);

    private static WebApplicationFactory<Program> WithClock(AuthApiFactory factory, DateTimeOffset utcNow) =>
        factory.WithWebHostBuilder(b => b.ConfigureServices(services =>
        {
            foreach (var d in services.Where(d => d.ServiceType == typeof(TimeProvider)).ToList())
            {
                services.Remove(d);
            }
            services.AddSingleton<TimeProvider>(new FixedTimeProvider(utcNow));
        }));

    private static async Task<Guid> GetUserIdAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        return (await userManager.FindByEmailAsync(AuthApiFactory.AllowedEmail))!.Id;
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
        WebApplicationFactory<Program> factory, Guid userId, Guid accountId, DateOnly date, decimal amount,
        Guid? categoryId, bool isInternalTransfer = false)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Transactions.Add(new Transaction
        {
            Id = Guid.NewGuid(), UserId = userId, AccountId = accountId, Date = date, Description = "txn",
            Amount = amount, Hash = Guid.NewGuid().ToString(), CategoryId = categoryId,
            IsInternalTransfer = isInternalTransfer, TransferFlagManuallySet = isInternalTransfer,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<(HttpClient Client, WebApplicationFactory<Program> Factory, Guid UserId, Guid AccountId)> SetUpAsync(DateTimeOffset now)
    {
        var baseFactory = new AuthApiFactory();
        var factory = WithClock(baseFactory, now);
        var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var userId = await GetUserIdAsync(factory);
        var accountId = await SeedAccountAsync(factory, userId, "111");
        return (client, factory, userId, accountId);
    }

    private static async Task<CategoryTrendDto> GetTrendAsync(HttpClient client, string query)
    {
        var response = await client.GetAsync($"/api/dashboard/category-trend?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CategoryTrendDto>(JsonOptions))!;
    }

    [Fact]
    public async Task CategoryTrend_WithoutAuthCookie_ReturnsUnauthorized()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/dashboard/category-trend?granularity=month&kind=spend&from=2099-01-01&to=2099-03-01");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CategoryTrend_Month_SumsPerBucketAndZeroFillsEmptyOnes()
    {
        var (client, factory, userId, accountId) = await SetUpAsync(March10);
        using var _ = client;
        await SeedAsync(factory, userId, accountId, new DateOnly(2099, 1, 5), -10m, GroceriesId);
        await SeedAsync(factory, userId, accountId, new DateOnly(2099, 1, 31), -5m, GroceriesId);
        await SeedAsync(factory, userId, accountId, new DateOnly(2099, 3, 1), -20m, GroceriesId);

        var trend = await GetTrendAsync(client, "granularity=month&kind=spend&from=2099-01-01&to=2099-03-10");

        Assert.Equal(3, trend.Buckets.Count);
        Assert.Equal(new DateOnly(2099, 2, 1), trend.Buckets[1].Start);
        Assert.Equal(new DateOnly(2099, 2, 28), trend.Buckets[1].End);
        var series = Assert.Single(trend.Series);
        Assert.Equal(GroceriesId, series.CategoryId);
        Assert.Equal(new[] { 15m, 0m, 20m }, series.Amounts);
        Assert.Equal(35m, series.Total);
    }

    [Fact]
    public async Task CategoryTrend_Week_StartsOnMondayAndSpansTheYearBoundary()
    {
        var (client, factory, userId, accountId) = await SetUpAsync(Jan20);
        using var _ = client;
        // 2099-12-28 and 2100-01-04 are Mondays; the first week straddles New Year.
        await SeedAsync(factory, userId, accountId, new DateOnly(2099, 12, 31), -5m, GroceriesId);
        await SeedAsync(factory, userId, accountId, new DateOnly(2100, 1, 3), -7m, GroceriesId);
        await SeedAsync(factory, userId, accountId, new DateOnly(2100, 1, 4), -11m, GroceriesId);

        var trend = await GetTrendAsync(client, "granularity=week&kind=spend&from=2099-12-28&to=2100-01-10");

        Assert.Equal(2, trend.Buckets.Count);
        Assert.Equal(new DateOnly(2099, 12, 28), trend.Buckets[0].Start);
        Assert.Equal(new DateOnly(2100, 1, 3), trend.Buckets[0].End);
        Assert.Equal(new[] { 12m, 11m }, Assert.Single(trend.Series).Amounts);
    }

    [Fact]
    public async Task CategoryTrend_EdgeBucketsAreClippedToTheRange()
    {
        var (client, factory, userId, accountId) = await SetUpAsync(Jan20);
        using var _ = client;
        await SeedAsync(factory, userId, accountId, new DateOnly(2099, 12, 28), -100m, GroceriesId); // before `from`
        await SeedAsync(factory, userId, accountId, new DateOnly(2099, 12, 30), -3m, GroceriesId);
        await SeedAsync(factory, userId, accountId, new DateOnly(2100, 1, 6), -4m, GroceriesId);
        await SeedAsync(factory, userId, accountId, new DateOnly(2100, 1, 8), -100m, GroceriesId); // after `to`

        var trend = await GetTrendAsync(client, "granularity=week&kind=spend&from=2099-12-30&to=2100-01-07");

        Assert.Equal(new DateOnly(2099, 12, 30), trend.Buckets[0].Start);
        Assert.Equal(new DateOnly(2100, 1, 7), trend.Buckets[^1].End);
        Assert.Equal(7m, Assert.Single(trend.Series).Total);
    }

    [Fact]
    public async Task CategoryTrend_Year_GroupsByCalendarYear()
    {
        var (client, factory, userId, accountId) = await SetUpAsync(Jan20);
        using var _ = client;
        await SeedAsync(factory, userId, accountId, new DateOnly(2098, 6, 1), -10m, GroceriesId);
        await SeedAsync(factory, userId, accountId, new DateOnly(2099, 1, 1), -20m, GroceriesId);
        await SeedAsync(factory, userId, accountId, new DateOnly(2099, 12, 31), -30m, GroceriesId);

        var trend = await GetTrendAsync(client, "granularity=year&kind=spend&from=2098-01-01&to=2100-01-20");

        Assert.Equal(3, trend.Buckets.Count);
        Assert.Equal(new[] { 10m, 50m, 0m }, Assert.Single(trend.Series).Amounts);
    }

    [Fact]
    public async Task CategoryTrend_ExcludesUncategorizedAndInternalTransfers()
    {
        var (client, factory, userId, accountId) = await SetUpAsync(March10);
        using var _ = client;
        var day = new DateOnly(2099, 3, 2);
        await SeedAsync(factory, userId, accountId, day, -50m, null);
        await SeedAsync(factory, userId, accountId, day, -300m, GroceriesId, isInternalTransfer: true);
        await SeedAsync(factory, userId, accountId, day, -8m, GroceriesId);

        var trend = await GetTrendAsync(client, "granularity=month&kind=spend&from=2099-03-01&to=2099-03-10");

        Assert.Equal(8m, Assert.Single(trend.Series).Total);
    }

    [Fact]
    public async Task CategoryTrend_KindSelectsSignAndReportsPositiveMagnitudes()
    {
        var (client, factory, userId, accountId) = await SetUpAsync(March10);
        using var _ = client;
        var day = new DateOnly(2099, 3, 2);
        await SeedAsync(factory, userId, accountId, day, -12m, GroceriesId);
        await SeedAsync(factory, userId, accountId, day, 100m, DiningId);

        var spend = await GetTrendAsync(client, "granularity=month&kind=spend&from=2099-03-01&to=2099-03-10");
        var income = await GetTrendAsync(client, "granularity=month&kind=income&from=2099-03-01&to=2099-03-10");

        var spendSeries = Assert.Single(spend.Series);
        Assert.Equal(GroceriesId, spendSeries.CategoryId);
        Assert.Equal(12m, spendSeries.Total);
        var incomeSeries = Assert.Single(income.Series);
        Assert.Equal(DiningId, incomeSeries.CategoryId);
        Assert.Equal(100m, incomeSeries.Total);
    }

    [Fact]
    public async Task CategoryTrend_OnlyIncludesCallingUsersTransactions()
    {
        var (client, factory, userId, accountId) = await SetUpAsync(March10);
        using var _ = client;
        var otherUserId = Guid.NewGuid();
        var otherAccountId = await SeedAccountAsync(factory, otherUserId, "222");
        var day = new DateOnly(2099, 3, 2);
        await SeedAsync(factory, userId, accountId, day, -4m, GroceriesId);
        await SeedAsync(factory, otherUserId, otherAccountId, day, -999m, DiningId);

        var trend = await GetTrendAsync(client, "granularity=month&kind=spend&from=2099-03-01&to=2099-03-10");

        var series = Assert.Single(trend.Series);
        Assert.Equal(4m, series.Total);
    }

    [Fact]
    public async Task CategoryTrend_OmitsCategoriesWithNoDataInRange()
    {
        var (client, factory, userId, accountId) = await SetUpAsync(March10);
        using var _ = client;
        await SeedAsync(factory, userId, accountId, new DateOnly(2099, 1, 2), -40m, DiningId); // outside range
        await SeedAsync(factory, userId, accountId, new DateOnly(2099, 3, 2), -4m, GroceriesId);

        var trend = await GetTrendAsync(client, "granularity=month&kind=spend&from=2099-03-01&to=2099-03-10");

        Assert.DoesNotContain(trend.Series, s => s.CategoryId == DiningId);
    }

    [Fact]
    public async Task CategoryTrend_CurrentUnfinishedBucketIsAccepted()
    {
        var (client, factory, userId, accountId) = await SetUpAsync(March10);
        using var _ = client;
        await SeedAsync(factory, userId, accountId, new DateOnly(2099, 3, 10), -4m, GroceriesId);

        var trend = await GetTrendAsync(client, "granularity=month&kind=spend&from=2099-03-01&to=2099-03-10");

        var bucket = Assert.Single(trend.Buckets);
        Assert.Equal(new DateOnly(2099, 3, 10), bucket.End);
    }

    [Theory]
    [InlineData("granularity=day&kind=spend&from=2099-03-01&to=2099-03-10")]
    [InlineData("kind=spend&from=2099-03-01&to=2099-03-10")]
    [InlineData("granularity=month&kind=both&from=2099-03-01&to=2099-03-10")]
    [InlineData("granularity=month&from=2099-03-01&to=2099-03-10")]
    [InlineData("granularity=month&kind=spend&from=2099-03-10&to=2099-03-01")]
    [InlineData("granularity=month&kind=spend&from=2099-03-01&to=2099-03-11")]
    [InlineData("granularity=week&kind=spend&from=2096-01-01&to=2099-03-10")]
    [InlineData("granularity=month&kind=spend&from=2088-01-01&to=2099-03-10")]
    public async Task CategoryTrend_WithInvalidParameters_ReturnsBadRequest(string query)
    {
        var (client, _, _, _) = await SetUpAsync(March10);
        using var _c = client;

        var response = await client.GetAsync($"/api/dashboard/category-trend?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CategoryTrend_ExactlyMaxBuckets_IsAccepted()
    {
        var (client, _, _, _) = await SetUpAsync(March10);
        using var _c = client;

        // 2089-04 .. 2099-03 is 120 months.
        var trend = await GetTrendAsync(client, "granularity=month&kind=spend&from=2089-04-01&to=2099-03-10");

        Assert.Equal(120, trend.Buckets.Count);
    }
}
