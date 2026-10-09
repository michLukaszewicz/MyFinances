using MyFinances.Api.Categorization;
using MyFinances.Api.Dashboard;
using MyFinances.Api.Import;
using MyFinances.Api.Tests.Support;
using MyFinances.Api.Transactions;
using System.Net;
using Xunit;

namespace MyFinances.Api.Tests.Ownership;

// Risk #3: user B must never read user A's data, in lists, aggregates or dedup answers. Expected
// sums are authored below from the seeded literals, not read back from the endpoints.
//
// Standard setup (InitializeAsync): A has a categorized expense (-100.00), an income (+3000.00) and
// an uncategorized row (-50.00) on one account; B has one categorized expense (-30.00) on another.
// All rows are dated inside August 2026.
public class DataOwnershipReadTests : IAsyncLifetime
{
    private const string Period = "from=2026-08-01&to=2026-08-31";
    private static readonly DateOnly Day = new(2026, 8, 10);
    private Account accountA = null!;
    private Account accountB = null!;
    private Transaction expenseA = null!;
    private Transaction expenseB = null!;
    private Guid expenseCategoryId;
    private Guid expenseCategoryIdB;
    private TwoUserHarness harness = null!;
    private Transaction incomeA = null!;
    private Guid incomeCategoryId;
    private Transaction uncategorizedA = null!;

    [Fact]
    public async Task AccountsList_ShowsEachUserOnlyTheirOwnAccounts()
    {
        // Act
        var forB = await TwoUserHarness.GetJsonAsync<List<AccountDto>>(harness.ClientB, "/api/accounts/");
        // Assert
        Assert.Equal([accountB.Id], forB.Select(a => a.Id));
        Assert.Equal(["222"], forB.Select(a => a.AccountNumber));
    }

    [Fact]
    public async Task CategoriesList_AfterOneUserDeletesACategory_StillHasAllOfTheOtherUsers()
    {
        // Arrange
        var before = await TwoUserHarness.GetJsonAsync<List<CategoryDto>>(harness.ClientB, "/api/categorization/categories");
        var deleted = await TwoUserHarness.SendAsync(
            harness.ClientA, HttpMethod.Delete, $"/api/categorization/categories/{incomeCategoryId}?uncategorizeTransactions=true");
        // Act
        var forA = await TwoUserHarness.GetJsonAsync<List<CategoryDto>>(harness.ClientA, "/api/categorization/categories");
        var forB = await TwoUserHarness.GetJsonAsync<List<CategoryDto>>(harness.ClientB, "/api/categorization/categories");
        // Assert
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.DoesNotContain(forA, c => c.Id == incomeCategoryId);
        Assert.Equal(before.Select(c => c.Id), forB.Select(c => c.Id));
    }

    [Fact]
    public async Task CategoriesList_NeverContainsAnotherUsersCustomCategory()
    {
        // Arrange
        var customA = await harness.SeedCustomCategoryAsync(harness.UserIdA, "Pets A");
        // Act
        var forB = await TwoUserHarness.GetJsonAsync<List<CategoryDto>>(harness.ClientB, "/api/categorization/categories");
        var forA = await TwoUserHarness.GetJsonAsync<List<CategoryDto>>(harness.ClientA, "/api/categorization/categories");
        // Assert
        Assert.DoesNotContain(forB, c => c.Id == customA.Id);
        Assert.Contains(forA, c => c.Id == customA.Id);
    }

    [Fact]
    public async Task CategorizationQueueAndHandled_NeverContainAnotherUsersRows()
    {
        // Act
        var queueForB = await TwoUserHarness.GetJsonAsync<List<TransactionQueueItemDto>>(harness.ClientB, "/api/categorization/queue");
        var handledForB = await TwoUserHarness.GetJsonAsync<List<TransactionQueueItemDto>>(harness.ClientB, "/api/categorization/handled");
        // Assert
        Assert.Empty(queueForB);
        Assert.Equal([expenseB.Id], handledForB.Select(t => t.Id));
    }

    [Fact]
    public async Task DashboardIncome_ForUserWithoutIncome_IsEmptyEvenThoughAnotherUserHasSome()
    {
        // Act
        var forB = await TwoUserHarness.GetJsonAsync<List<CategorySpendDto>>(harness.ClientB, $"/api/dashboard/category-income?{Period}");
        var forA = await TwoUserHarness.GetJsonAsync<List<CategorySpendDto>>(harness.ClientA, $"/api/dashboard/category-income?{Period}");
        // Assert
        Assert.Empty(forB);
        Assert.Equal(3000.00m, Assert.Single(forA).Amount);
    }

    [Fact]
    public async Task DashboardSpend_SumsOnlyTheCallersOwnRows()
    {
        // Act
        var forB = await TwoUserHarness.GetJsonAsync<List<CategorySpendSignalDto>>(harness.ClientB, $"/api/dashboard/category-spend?{Period}");
        var forA = await TwoUserHarness.GetJsonAsync<List<CategorySpendSignalDto>>(harness.ClientA, $"/api/dashboard/category-spend?{Period}");
        // Assert
        var spendB = Assert.Single(forB);
        Assert.Equal(expenseCategoryIdB, spendB.CategoryId);
        Assert.Equal(30.00m, spendB.Amount);
        Assert.Equal(100.00m, Assert.Single(forA).Amount);
    }

    [Fact]
    public async Task DashboardTrend_SumsOnlyTheCallersOwnRows()
    {
        // Act
        var spendForB = await TwoUserHarness.GetJsonAsync<CategoryTrendDto>(
            harness.ClientB, $"/api/dashboard/category-trend?granularity=month&kind=spend&{Period}");
        var incomeForB = await TwoUserHarness.GetJsonAsync<CategoryTrendDto>(
            harness.ClientB, $"/api/dashboard/category-trend?granularity=month&kind=income&{Period}");
        // Assert
        Assert.Equal(30.00m, Assert.Single(spendForB.Series).Total);
        Assert.Empty(incomeForB.Series);
    }

    public Task DisposeAsync()
    {
        harness.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task EveryReadEndpoint_WithoutASession_IsUnauthorized()
    {
        // Arrange
        using var anonymous = harness.Factory.CreateClient();
        var urls = new[]
        {
            "/api/transactions",
            "/api/accounts/",
            "/api/categorization/queue",
            "/api/categorization/handled",
            $"/api/dashboard/category-spend?{Period}",
            $"/api/dashboard/category-income?{Period}",
            $"/api/dashboard/category-trend?granularity=month&kind=spend&{Period}",
        };
        // Act
        var statuses = new List<HttpStatusCode>();
        foreach (var url in urls)
        {
            statuses.Add((await anonymous.GetAsync(url)).StatusCode);
        }
        // Assert
        Assert.All(statuses, status => Assert.Equal(HttpStatusCode.Unauthorized, status));
    }

    [Fact]
    public async Task ImportParse_OfARowIdenticalToAnotherUsersRow_IsNotFlaggedAsDuplicate()
    {
        // Arrange
        var identicalToA = new MBankCsvRow(Day, "Test description A1", -100.00m);
        var accountOfB = await ImportTestHelpers.CreateAccountAsync(harness.ClientB, "mBank", "444");
        var statement = MBankCsvBuilder.Build([identicalToA]);
        // Act
        var parsed = await ImportTestHelpers.ParseAsync(harness.ClientB, statement, accountOfB.Id);
        // Assert
        var row = Assert.Single(parsed.Rows);
        Assert.False(row.IsDuplicate);
        Assert.Null(row.ExistingTransaction);
        Assert.Equal(0, parsed.MixedFormatOverlapCount);
    }

    public async Task InitializeAsync()
    {
        harness = await TwoUserHarness.CreateAsync();
        (expenseCategoryId, _, incomeCategoryId) = await harness.GetCategoryIdsAsync(harness.UserIdA);
        (expenseCategoryIdB, _, _) = await harness.GetCategoryIdsAsync(harness.UserIdB);
        accountA = await harness.SeedAccountAsync(harness.UserIdA, "111");
        accountB = await harness.SeedAccountAsync(harness.UserIdB, "222");
        expenseA = await harness.SeedTransactionAsync(harness.UserIdA, accountA.Id, Day, -100.00m, "Test description A1", expenseCategoryId);
        incomeA = await harness.SeedTransactionAsync(harness.UserIdA, accountA.Id, Day, 3000.00m, "Test description A2", incomeCategoryId);
        uncategorizedA = await harness.SeedTransactionAsync(harness.UserIdA, accountA.Id, Day, -50.00m, "Test description A3");
        expenseB = await harness.SeedTransactionAsync(harness.UserIdB, accountB.Id, Day, -30.00m, "Test description B1", expenseCategoryIdB);
    }

    [Fact]
    public async Task TransactionsList_FilteredByAnotherUsersCategoryAndPeriod_ReturnsOnlyOwnRows()
    {
        // Act
        var forB = await TwoUserHarness.GetJsonAsync<TransactionListResponseDto>(
            harness.ClientB, $"/api/transactions?categoryId={incomeCategoryId}&{Period}&kind=income");
        var forBExpense = await TwoUserHarness.GetJsonAsync<TransactionListResponseDto>(
            harness.ClientB, $"/api/transactions?categoryId={expenseCategoryIdB}&{Period}");
        // Assert
        Assert.Empty(forB.Items);
        Assert.Equal([expenseB.Id], forBExpense.Items.Select(t => t.Id));
    }

    [Fact]
    public async Task TransactionsList_ShowsEachUserOnlyTheirOwnRows()
    {
        // Act
        var forB = await TwoUserHarness.GetJsonAsync<TransactionListResponseDto>(harness.ClientB, "/api/transactions?take=100");
        var forA = await TwoUserHarness.GetJsonAsync<TransactionListResponseDto>(harness.ClientA, "/api/transactions?take=100");
        // Assert
        Assert.Equal([expenseB.Id], forB.Items.Select(t => t.Id));
        Assert.Equal(
            new[] { expenseA.Id, incomeA.Id, uncategorizedA.Id }.OrderBy(id => id),
            forA.Items.Select(t => t.Id).OrderBy(id => id));
    }

    [Fact]
    public async Task TransferDetection_DoesNotPairOppositeAmountsAcrossUsers()
    {
        // Arrange
        var outgoingA = await harness.SeedTransactionAsync(harness.UserIdA, accountA.Id, Day, -777.00m, "Test description A4");
        var incomingB = await harness.SeedTransactionAsync(harness.UserIdB, accountB.Id, Day, 777.00m, "Test description B2");
        // Act
        var queueForA = await TwoUserHarness.GetJsonAsync<List<TransactionQueueItemDto>>(harness.ClientA, "/api/categorization/queue");
        var queueForB = await TwoUserHarness.GetJsonAsync<List<TransactionQueueItemDto>>(harness.ClientB, "/api/categorization/queue");
        // Assert
        Assert.False(queueForA.Single(t => t.Id == outgoingA.Id).IsInternalTransfer);
        Assert.False(queueForB.Single(t => t.Id == incomingB.Id).IsInternalTransfer);
    }

    [Fact]
    public async Task UncategorizedCount_CountsOnlyTheCallersOwnRows()
    {
        // Act
        var forA = await TwoUserHarness.GetJsonAsync<UncategorizedCountDto>(harness.ClientA, "/api/categorization/queue/count");
        var forB = await TwoUserHarness.GetJsonAsync<UncategorizedCountDto>(harness.ClientB, "/api/categorization/queue/count");
        // Assert
        Assert.Equal(1, forA.Count);
        Assert.Equal(0, forB.Count);
    }
}