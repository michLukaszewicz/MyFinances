using MyFinances.Api.Import;
using MyFinances.Api.Tests.Support;
using MyFinances.Api.Transactions;
using System.Net;
using Xunit;

namespace MyFinances.Api.Tests.Ownership;

// Risk #3: user B must never change user A's data. Two real, separately authenticated users share
// one host. Every denied attempt asserts the status AND that both users' persisted rows are
// unchanged, read straight from the database.
//
// Standard setup (InitializeAsync): user A owns an account with a categorized transaction, an
// uncategorized one and an imported one in a batch; user B owns an account with one transaction.
public class DataOwnershipWriteTests : IAsyncLifetime
{
    private static readonly DateOnly Day = new(2026, 8, 10);
    private Account accountA = null!;
    private Account accountB = null!;
    private Transaction categorizedA = null!;
    private Guid expenseCategoryId;
    private Guid expenseCategoryIdB;
    private TwoUserHarness harness = null!;
    private Transaction importedA = null!;
    private Guid secondExpenseCategoryId;
    private Guid secondExpenseCategoryIdB;
    private List<string> snapshotA = null!;
    private List<string> snapshotB = null!;
    private Transaction transactionB = null!;
    private Transaction uncategorizedA = null!;

    [Fact]
    public async Task AccountsDelete_OnAnotherUsersAccount_IsNotFoundAndLeavesDataUnchanged()
    {
        // Arrange
        var emptyAccountA = await harness.SeedAccountAsync(harness.UserIdA, "333");
        snapshotA = await harness.SnapshotAsync(harness.UserIdA);
        // Act
        var response = await TwoUserHarness.SendAsync(harness.ClientB, HttpMethod.Delete, $"/api/accounts/{emptyAccountA.Id}");
        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertNobodysDataChangedAsync();
    }

    [Fact]
    public async Task AccountsPut_OnAnotherUsersAccount_IsNotFoundAndLeavesDataUnchanged()
    {
        // Act
        var response = await TwoUserHarness.SendAsync(
            harness.ClientB, HttpMethod.Put, $"/api/accounts/{accountA.Id}", new AccountWriteRequest("Hijacked", "999"));
        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertNobodysDataChangedAsync();
    }

    [Fact]
    public async Task CategoriesDelete_OnAnotherUsersCustomCategory_IsNotFoundAndLeavesDataUnchanged()
    {
        // Arrange
        var customA = await harness.SeedCustomCategoryAsync(harness.UserIdA, "Pets A");
        snapshotA = await harness.SnapshotAsync(harness.UserIdA);
        // Act
        var response = await TwoUserHarness.SendAsync(
            harness.ClientB, HttpMethod.Delete, $"/api/categorization/categories/{customA.Id}?uncategorizeTransactions=true");
        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertNobodysDataChangedAsync();
    }

    [Fact]
    public async Task CategorizePut_OnAnotherUsersTransaction_IsNotFoundAndLeavesDataUnchanged()
    {
        // Arrange
        var categorize = new { CategoryId = secondExpenseCategoryIdB, IsInternalTransfer = true };
        // Act
        var response = await TwoUserHarness.SendAsync(
            harness.ClientB, HttpMethod.Put, $"/api/categorization/transactions/{uncategorizedA.Id}", categorize);
        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertNobodysDataChangedAsync();
    }

    [Fact]
    public async Task CategorizePut_WithAnotherUsersCustomCategory_IsBadRequestAndLeavesDataUnchanged()
    {
        // Arrange
        var customA = await harness.SeedCustomCategoryAsync(harness.UserIdA, "Pets A");
        snapshotA = await harness.SnapshotAsync(harness.UserIdA);
        var categorize = new { CategoryId = customA.Id };
        // Act
        var response = await TwoUserHarness.SendAsync(
            harness.ClientB, HttpMethod.Put, $"/api/categorization/transactions/{transactionB.Id}", categorize);
        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertNobodysDataChangedAsync();
    }

    public Task DisposeAsync()
    {
        harness.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task ImportCommit_IntoAnotherUsersAccount_IsNotFoundAndLeavesDataUnchanged()
    {
        // Arrange
        var commit = new
        {
            AccountId = accountA.Id,
            SkippedErrorCount = 0,
            Rows = new[] { new { Date = Day, Description = "Planted", Amount = -9.00m, Decision = RowDecision.Keep.ToString() } },
        };
        // Act
        var response = await TwoUserHarness.SendAsync(harness.ClientB, HttpMethod.Post, "/api/import/commit", commit);
        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertNobodysDataChangedAsync();
    }

    [Fact]
    public async Task ImportParse_AgainstAnotherUsersAccount_IsNotFoundAndLeavesDataUnchanged()
    {
        // Arrange
        var statement = MBankCsvBuilder.Build([new MBankCsvRow(Day, "Test description A1", -100.00m)]);
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(statement), "file", "export.csv");
        content.Add(new StringContent(accountA.Id.ToString()), "accountId");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/import/parse") { Content = content };
        request.Headers.Add("X-XSRF-TOKEN", await ImportTestHelpers.GetAntiforgeryTokenAsync(harness.ClientB));
        // Act
        var response = await harness.ClientB.SendAsync(request);
        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertNobodysDataChangedAsync();
    }

    public async Task InitializeAsync()
    {
        harness = await TwoUserHarness.CreateAsync();
        (expenseCategoryId, secondExpenseCategoryId, _) = await harness.GetCategoryIdsAsync(harness.UserIdA);
        (expenseCategoryIdB, secondExpenseCategoryIdB, _) = await harness.GetCategoryIdsAsync(harness.UserIdB);
        accountA = await harness.SeedAccountAsync(harness.UserIdA, "111");
        accountB = await harness.SeedAccountAsync(harness.UserIdB, "222");
        var batchA = await harness.SeedImportBatchAsync(harness.UserIdA, accountA.Id, 1);
        categorizedA = await harness.SeedTransactionAsync(harness.UserIdA, accountA.Id, Day, -100.00m, "Test description A1", expenseCategoryId);
        uncategorizedA = await harness.SeedTransactionAsync(harness.UserIdA, accountA.Id, Day, -50.00m, "Test description A2");
        importedA = await harness.SeedTransactionAsync(harness.UserIdA, accountA.Id, Day, -25.00m, "Test description A3", null, batchA.Id);
        transactionB = await harness.SeedTransactionAsync(harness.UserIdB, accountB.Id, Day, -30.00m, "Test description B1", expenseCategoryIdB);
        snapshotA = await harness.SnapshotAsync(harness.UserIdA);
        snapshotB = await harness.SnapshotAsync(harness.UserIdB);
    }

    [Fact]
    public async Task OwnerCanStillChangeOwnData_ControlForTheDenialTests()
    {
        // Arrange
        var edit = new TransactionWriteRequest(Day, "Edited by owner", -101.00m, accountA.Id, secondExpenseCategoryId, true);
        // Act
        var response = await TwoUserHarness.SendAsync(harness.ClientA, HttpMethod.Put, $"/api/transactions/{categorizedA.Id}", edit);
        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(snapshotA, await harness.SnapshotAsync(harness.UserIdA));
        Assert.Equal(snapshotB, await harness.SnapshotAsync(harness.UserIdB));
    }

    [Fact]
    public async Task TransactionsDelete_OnAnotherUsersImportedTransaction_IsNotFoundAndLeavesDataUnchanged()
    {
        // Act
        var response = await TwoUserHarness.SendAsync(harness.ClientB, HttpMethod.Delete, $"/api/transactions/{importedA.Id}");
        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertNobodysDataChangedAsync();
    }

    [Fact]
    public async Task TransactionsPost_IntoAnotherUsersAccount_IsBadRequestAndLeavesDataUnchanged()
    {
        // Arrange
        var create = new TransactionWriteRequest(Day, "Planted", -5.00m, accountA.Id, expenseCategoryIdB, false);
        // Act
        var response = await TwoUserHarness.SendAsync(harness.ClientB, HttpMethod.Post, "/api/transactions/", create);
        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertNobodysDataChangedAsync();
    }

    [Fact]
    public async Task TransactionsPost_WithAnotherUsersCustomCategory_IsBadRequestAndLeavesDataUnchanged()
    {
        // Arrange
        var customA = await harness.SeedCustomCategoryAsync(harness.UserIdA, "Pets A");
        snapshotA = await harness.SnapshotAsync(harness.UserIdA);
        var create = new TransactionWriteRequest(Day, "Planted", -5.00m, accountB.Id, customA.Id, false);
        // Act
        var response = await TwoUserHarness.SendAsync(harness.ClientB, HttpMethod.Post, "/api/transactions/", create);
        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertNobodysDataChangedAsync();
    }

    [Fact]
    public async Task TransactionsPut_MovingOwnTransactionIntoAnotherUsersAccount_IsBadRequestAndLeavesDataUnchanged()
    {
        // Arrange
        var move = new TransactionWriteRequest(Day, "Test description B1", -30.00m, accountA.Id, expenseCategoryIdB, false);
        // Act
        var response = await TwoUserHarness.SendAsync(harness.ClientB, HttpMethod.Put, $"/api/transactions/{transactionB.Id}", move);
        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertNobodysDataChangedAsync();
    }

    [Fact]
    public async Task TransactionsPut_OnAnotherUsersTransaction_IsNotFoundAndLeavesDataUnchanged()
    {
        // Arrange
        var edit = new TransactionWriteRequest(Day, "Overwritten", -1.00m, accountB.Id, secondExpenseCategoryIdB, true);
        // Act
        var response = await TwoUserHarness.SendAsync(harness.ClientB, HttpMethod.Put, $"/api/transactions/{categorizedA.Id}", edit);
        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertNobodysDataChangedAsync();
    }

    private async Task AssertNobodysDataChangedAsync()
    {
        Assert.Equal(snapshotA, await harness.SnapshotAsync(harness.UserIdA));
        Assert.Equal(snapshotB, await harness.SnapshotAsync(harness.UserIdB));
    }
}