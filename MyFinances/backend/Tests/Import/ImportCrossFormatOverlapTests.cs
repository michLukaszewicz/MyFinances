using MyFinances.Api.Import;
using MyFinances.Api.Tests.Support;
using MyFinances.Api.Transactions;
using Xunit;
using MyFinances.Api.Tests.Auth;

namespace MyFinances.Api.Tests.Import;

// Cross-format overlap: an mBank PDF and an mBank CSV of the same account describe the same
// transactions with different descriptions, so dedup cannot match them; the parse response warns
// through MixedFormatOverlapCount instead. Both files are built from one authored list
// (MBankPairedStatement), and expected values come from that list, never from a parser run.
//
// Standard setup for every test (InitializeAsync): a fresh app, an authenticated user and an
// empty mBank account. A test's Arrange section holds only what differs from that.
public class ImportCrossFormatOverlapTests : IAsyncLifetime
{
    private static readonly IReadOnlyList<MBankPairedTransaction> All = MBankPairedStatement.Transactions;

    // The middle three rows: the CSV of a shorter period than the PDF.
    private static readonly IReadOnlyList<MBankPairedTransaction> Middle = All.Skip(1).Take(3).ToList();

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

    private Task<ImportParseResponse> ParseCsvAsync(IEnumerable<MBankPairedTransaction>? subset = null) =>
        ImportTestHelpers.ParseAsync(_client, MBankPairedStatement.BuildCsv(subset), _account.Id);

    private Task<ImportParseResponse> ParsePdfAsync() =>
        ImportTestHelpers.ParsePdfAsync(_client, MBankPairedStatement.BuildPdf(), _account.Id);

    private Task<List<Transaction>> GetStoredAsync() => ImportTestHelpers.GetStoredAsync(_factory, _account.Id);

    private static List<(DateOnly Date, decimal Amount)> DateAmounts(IEnumerable<MBankPairedTransaction> transactions) =>
        transactions.Select(t => (t.Date, t.Amount)).OrderBy(x => x.Date).ThenBy(x => x.Amount).ToList();

    private static List<(DateOnly Date, decimal Amount)> DateAmounts(ImportParseResponse parsed) =>
        parsed.Rows.Select(r => (r.Date, r.Amount)).OrderBy(x => x.Date).ThenBy(x => x.Amount).ToList();

    // Rows of the committed format that fall inside the date range of the file being parsed.
    private static int ExpectedOverlap(IReadOnlyList<MBankPairedTransaction> committed, IReadOnlyList<MBankPairedTransaction> parsed)
    {
        var min = parsed.Min(t => t.Date);
        var max = parsed.Max(t => t.Date);
        return committed.Count(t => t.Date >= min && t.Date <= max);
    }

    [Fact]
    public async Task PairedFiles_ParseToTheSameDatesAndAmounts_AsTheAuthoredList()
    {
        // Act
        var csv = await ParseCsvAsync();
        var pdf = await ParsePdfAsync();

        // Assert
        var expected = DateAmounts(All);
        Assert.Equal(StatementFormat.Csv, csv.Format);
        Assert.Equal(StatementFormat.Pdf, pdf.Format);
        Assert.Equal(expected, DateAmounts(csv));
        Assert.Equal(expected, DateAmounts(pdf));
    }

    [Fact]
    public async Task ParseCsv_AfterPdfCommitted_WarnsAboutOverlapAndFlagsNoRowAsDuplicate()
    {
        // Arrange
        await ImportTestHelpers.CommitAllAsync(_client, _account.Id, await ParsePdfAsync());

        // Act
        var csv = await ParseCsvAsync();

        // Assert
        Assert.Equal(All.Count, csv.Rows.Count);
        Assert.Equal(ExpectedOverlap(All, All), csv.MixedFormatOverlapCount);
        Assert.Equal(All.Count, csv.MixedFormatOverlapCount);
        Assert.All(csv.Rows, row => Assert.False(row.IsDuplicate));
    }

    [Fact]
    public async Task ParsePdf_AfterCsvCommitted_WarnsAboutOverlapAndFlagsNoRowAsDuplicate()
    {
        // Arrange
        await ImportTestHelpers.CommitAllAsync(_client, _account.Id, await ParseCsvAsync());

        // Act
        var pdf = await ParsePdfAsync();

        // Assert
        Assert.Equal(All.Count, pdf.Rows.Count);
        Assert.Equal(ExpectedOverlap(All, All), pdf.MixedFormatOverlapCount);
        Assert.Equal(All.Count, pdf.MixedFormatOverlapCount);
        Assert.All(pdf.Rows, row => Assert.False(row.IsDuplicate));
    }

    [Fact]
    public async Task Control_ReparseSameCsvAfterCommit_FlagsEveryRowWithoutOverlapWarning()
    {
        // Arrange
        await ImportTestHelpers.CommitAllAsync(_client, _account.Id, await ParseCsvAsync());

        // Act
        var csv = await ParseCsvAsync();

        // Assert
        Assert.Equal(All.Count, csv.Rows.Count);
        Assert.All(csv.Rows, row => Assert.True(row.IsDuplicate));
        Assert.Equal(0, csv.MixedFormatOverlapCount);
    }

    [Fact]
    public async Task Control_ReparseSamePdfAfterCommit_FlagsEveryRowWithoutOverlapWarning()
    {
        // Arrange
        await ImportTestHelpers.CommitAllAsync(_client, _account.Id, await ParsePdfAsync());

        // Act
        var pdf = await ParsePdfAsync();

        // Assert
        Assert.Equal(All.Count, pdf.Rows.Count);
        Assert.All(pdf.Rows, row => Assert.True(row.IsDuplicate));
        Assert.Equal(0, pdf.MixedFormatOverlapCount);
    }

    // CHARACTERIZATION of the warn-only contract, not an endorsement: the overlap count is only a
    // warning, so a user who keeps every row of both imports stores each transaction twice and the
    // account total is doubled. This pins current behavior; update it if overlap ever blocks or
    // de-duplicates the commit.
    [Fact]
    public async Task Commit_KeepAllOfBothFormats_StoresBothImportsAndDoublesTheAmountSum()
    {
        // Arrange
        await ImportTestHelpers.CommitAllAsync(_client, _account.Id, await ParsePdfAsync());
        var csv = await ParseCsvAsync();

        // Act
        var summary = await ImportTestHelpers.CommitAllAsync(_client, _account.Id, csv);

        // Assert
        Assert.Equal(All.Count, summary.ImportedCount);
        var stored = await GetStoredAsync();
        Assert.Equal(All.Count * 2, stored.Count);
        Assert.Equal(MBankPairedStatement.AmountSum(All) * 2, stored.Sum(t => t.Amount));
        Assert.All(All, t => Assert.Equal(2, stored.Count(s => s.Date == t.Date && s.Amount == t.Amount)));
    }

    [Fact]
    public async Task ParseCsv_CoveringOnlyPartOfThePdfPeriod_CountsOnlyPdfRowsInsideItsDateRange()
    {
        // Arrange
        await ImportTestHelpers.CommitAllAsync(_client, _account.Id, await ParsePdfAsync());

        // Act
        var csv = await ParseCsvAsync(Middle);

        // Assert
        Assert.Equal(Middle.Count, csv.Rows.Count);
        Assert.Equal(ExpectedOverlap(All, Middle), csv.MixedFormatOverlapCount);
        Assert.Equal(3, csv.MixedFormatOverlapCount);
        Assert.NotEqual(All.Count, csv.MixedFormatOverlapCount);
        Assert.All(csv.Rows, row => Assert.False(row.IsDuplicate));
    }
}
