using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MyFinances.Api.Categorization;
using MyFinances.Api.Import;
using MyFinances.Api.Tests.Support;
using MyFinances.Api.Transactions;
using Xunit;
using MyFinances.Api.Tests.Auth;

namespace MyFinances.Api.Tests.Import;

// Dedup integrity: re-imports are flagged, skipped rows leave stored data unchanged, and the
// weak points of the dedup key (date + amount + description + account) are pinned. Expected
// rows are authored below, never read back from a first parse.
//
// Standard setup for every test (InitializeAsync): a fresh app, an authenticated user and an
// empty mBank account. A test's Arrange section holds only what differs from that.
public class ImportDedupIntegrityTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // The standard statement: four rows, two of them identical (same key).
    private static readonly MBankCsvRow RepeatedRow = new(new DateOnly(2026, 8, 1), "Test description 1", -500.00m);
    private static readonly MBankCsvRow SecondRow = new(new DateOnly(2026, 8, 2), "Test description 2", 250.50m);
    private static readonly MBankCsvRow ThirdRow = new(new DateOnly(2026, 8, 5), "Test description 3", -120.00m);
    private static readonly MBankCsvRow[] StandardRows = [RepeatedRow, RepeatedRow, SecondRow, ThirdRow];

    private const int StandardRowCount = 4;
    private const decimal StandardAmountSum = -500.00m - 500.00m + 250.50m - 120.00m;

    private AuthApiFactory _factory = null!;
    private HttpClient _client = null!;
    private AccountDto _account = null!;

    public async Task InitializeAsync()
    {
        _factory = new AuthApiFactory();
        _client = await TestClientHelpers.CreateAuthenticatedClientAsync(_factory);
        _account = await ImportTestHelpers.CreateAccountAsync(_client, "mBank", "111");
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private Task<ImportParseResponse> ParseAsync(params MBankCsvRow[] rows) =>
        ImportTestHelpers.ParseAsync(_client, MBankCsvBuilder.Build(rows.Length == 0 ? StandardRows : rows), _account.Id);

    private Task<List<Transaction>> GetStoredAsync() => ImportTestHelpers.GetStoredAsync(_factory, _account.Id);

    private static bool Matches(ImportParseRow row, MBankCsvRow expected) =>
        row.Date == expected.BookingDate && row.Amount == expected.Amount && row.Description == expected.Title;

    private static bool Matches(Transaction stored, MBankCsvRow expected) =>
        stored.Date == expected.BookingDate && stored.Amount == expected.Amount && stored.Description == expected.Title;

    [Fact]
    public async Task Reimport_FullFileAfterKeepAll_FlagsEveryRowAndSkippingStoresNothingNew()
    {
        // Arrange
        await ImportTestHelpers.CommitAllAsync(_client, _account.Id, await ParseAsync());
        var storedBefore = await GetStoredAsync();
        var second = await ParseAsync();

        // Act
        var summary = await ImportTestHelpers.CommitSkippingDuplicatesAsync(_client, _account.Id, second);

        // Assert
        Assert.Equal(StandardRowCount, storedBefore.Count);
        Assert.Equal(StandardAmountSum, storedBefore.Sum(t => t.Amount));
        Assert.Equal(StandardRowCount, second.Rows.Count);
        Assert.All(second.Rows, row =>
        {
            Assert.True(row.IsDuplicate);
            Assert.NotNull(row.ExistingTransaction);
        });
        Assert.Equal(0, summary.ImportedCount);
        Assert.Equal(StandardRowCount, summary.SkippedDuplicateCount);
        var storedAfter = await GetStoredAsync();
        Assert.Equal(StandardRowCount, storedAfter.Count);
        Assert.Equal(StandardAmountSum, storedAfter.Sum(t => t.Amount));
    }

    [Fact]
    public async Task Reimport_PartialOverlap_FlagsOnlyTheAlreadyStoredRows()
    {
        // Arrange: only the repeated pair and the second row were imported before.
        await ImportTestHelpers.CommitAllAsync(_client, _account.Id, await ParseAsync(RepeatedRow, RepeatedRow, SecondRow));
        var second = await ParseAsync();

        // Act
        await ImportTestHelpers.CommitSkippingDuplicatesAsync(_client, _account.Id, second);

        // Assert
        Assert.Equal(StandardRowCount, second.Rows.Count);
        Assert.Equal(3, second.Rows.Count(r => r.IsDuplicate));
        Assert.Equal(2, second.Rows.Count(r => r.IsDuplicate && Matches(r, RepeatedRow)));
        Assert.Single(second.Rows, r => r.IsDuplicate && Matches(r, SecondRow));
        var third = Assert.Single(second.Rows, r => Matches(r, ThirdRow));
        Assert.False(third.IsDuplicate);
        Assert.Null(third.ExistingTransaction);

        var stored = await GetStoredAsync();
        Assert.Equal(StandardRowCount, stored.Count);
        Assert.Equal(2, stored.Count(t => Matches(t, RepeatedRow)));
        Assert.Single(stored, t => Matches(t, SecondRow));
        Assert.Single(stored, t => Matches(t, ThirdRow));
        Assert.Equal(StandardAmountSum, stored.Sum(t => t.Amount));
    }

    // Pins the commit contract: only a Skip whose hash already exists is counted, and a skipped
    // row is never stored whether or not it was a duplicate.
    [Fact]
    public async Task Commit_SkipOnNonDuplicateRow_IsNotStoredAndNotCountedAsSkippedDuplicate()
    {
        // Arrange
        var parsed = await ParseAsync();

        // Act
        var summary = await ImportTestHelpers.CommitAsync(
            _client,
            _account.Id,
            parsed,
            r => Matches(r, ThirdRow) ? RowDecision.Skip : RowDecision.Keep);

        // Assert
        Assert.All(parsed.Rows, row => Assert.False(row.IsDuplicate));
        Assert.Equal(3, summary.ImportedCount);
        Assert.Equal(0, summary.SkippedDuplicateCount);
        var stored = await GetStoredAsync();
        Assert.Equal(3, stored.Count);
        Assert.DoesNotContain(stored, t => Matches(t, ThirdRow));
    }

    // The user's decision wins: Keep inserts even when the row is flagged as a duplicate.
    [Fact]
    public async Task Commit_KeepOnFlaggedDuplicate_StoresASecondRow()
    {
        // Arrange
        await ImportTestHelpers.CommitAllAsync(_client, _account.Id, await ParseAsync());
        var second = await ParseAsync();

        // Act
        var summary = await ImportTestHelpers.CommitAsync(
            _client,
            _account.Id,
            second,
            r => Matches(r, SecondRow) ? RowDecision.Keep : RowDecision.Skip);

        // Assert
        Assert.Single(second.Rows, r => Matches(r, SecondRow) && r.IsDuplicate);
        Assert.Equal(1, summary.ImportedCount);
        Assert.Equal(StandardRowCount - 1, summary.SkippedDuplicateCount);
        var stored = await GetStoredAsync();
        Assert.Equal(StandardRowCount + 1, stored.Count);
        Assert.Equal(2, stored.Count(t => Matches(t, SecondRow)));
    }

    [Fact]
    public async Task Parse_RowMatchingManuallyEnteredTransaction_IsFlaggedAsDuplicate()
    {
        // Arrange
        var categories = await (await _client.GetAsync("/api/categorization/categories"))
            .Content.ReadFromJsonAsync<List<CategoryDto>>(JsonOptions);
        var token = await ImportTestHelpers.GetAntiforgeryTokenAsync(_client);
        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/transactions/")
        {
            Content = JsonContent.Create(new TransactionWriteRequest(ThirdRow.BookingDate, ThirdRow.Title, ThirdRow.Amount, _account.Id, categories!.First().Id, false)),
        };
        createRequest.Headers.Add("X-XSRF-TOKEN", token);
        var created = await _client.SendAsync(createRequest);

        // Act
        var parsed = await ParseAsync();

        // Assert
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Null(Assert.Single(await GetStoredAsync()).ImportBatchId);
        Assert.Equal(StandardRowCount, parsed.Rows.Count);
        var flagged = Assert.Single(parsed.Rows, r => r.IsDuplicate);
        Assert.True(Matches(flagged, ThirdRow));
        Assert.NotNull(flagged.ExistingTransaction);
        Assert.Equal(ThirdRow.Title, flagged.ExistingTransaction!.Description);
        Assert.Equal(ThirdRow.Amount, flagged.ExistingTransaction.Amount);
    }

    // CHARACTERIZATION of a known limitation, not an endorsement (research.md Risk #1 / Open
    // Question 2): the dedup key has no occurrence counter, so with only ONE of the two identical
    // rows stored, parsing flags BOTH, and a skip-duplicates commit would drop a legitimate
    // second transaction. This test pins current behavior; update it if the key is ever changed.
    [Fact]
    public async Task Parse_WithOneOfTwoIdenticalRowsStored_FlagsBothIdenticalRows_KnownLimitation()
    {
        // Arrange
        await ImportTestHelpers.CommitAllAsync(_client, _account.Id, await ParseAsync(RepeatedRow));

        // Act
        var parsed = await ParseAsync();

        // Assert
        Assert.Equal(StandardRowCount, parsed.Rows.Count);
        var identical = parsed.Rows.Where(r => Matches(r, RepeatedRow)).ToList();
        Assert.Equal(2, identical.Count);
        Assert.All(identical, row => Assert.True(row.IsDuplicate));
        Assert.Equal(2, parsed.Rows.Count(r => r.IsDuplicate));
    }
}
