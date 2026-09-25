using MyFinances.Api.Import;
using Xunit;

namespace MyFinances.Api.Tests;

public class MBankCsvParserTests
{
    private static string FixturePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "mbank-sample-redacted.csv");

    private static FileStream OpenFixture() => File.OpenRead(FixturePath);

    [Fact]
    public void CanParse_ReturnsTrue_ForFixtureFile()
    {
        var parser = new MBankCsvParser();
        using var stream = OpenFixture();

        Assert.True(parser.CanParse(stream));
    }

    [Fact]
    public void Parse_ExtractsExactlyFourValidTransactions_IncludingBothBlikRows()
    {
        var parser = new MBankCsvParser();
        using var stream = OpenFixture();

        var result = parser.Parse(stream);

        Assert.Equal(4, result.Transactions.Count);

        var blikRows = result.Transactions
            .Where(t => t.Date == new DateOnly(2026, 8, 1) && t.Amount == -500.00m && t.Description == "NA JEDZENIE")
            .ToList();
        Assert.Equal(2, blikRows.Count);
    }

    [Fact]
    public void Parse_SkipsMalformedRow_WithoutAbortingRestOfFile()
    {
        var parser = new MBankCsvParser();
        using var stream = OpenFixture();

        var result = parser.Parse(stream);

        Assert.Contains(result.Transactions, t => t.Date == new DateOnly(2026, 8, 5) && t.Amount == -120.00m);
    }

    [Fact]
    public void Parse_ReportsOneSkippedErrorRow_ForFixture()
    {
        var parser = new MBankCsvParser();
        using var stream = OpenFixture();

        var result = parser.Parse(stream);

        Assert.Equal(1, result.SkippedErrorCount);
    }

    [Fact]
    public void Parse_ExcludesFooterRows_FromTransactionsAndErrorCount()
    {
        var parser = new MBankCsvParser();
        using var stream = OpenFixture();

        var result = parser.Parse(stream);

        Assert.DoesNotContain(result.Transactions, t => t.Description.Contains("informacyjny", StringComparison.OrdinalIgnoreCase));
        // 4 valid + 1 skipped-error == 5 data rows accounted for; footer rows contribute to neither.
        Assert.Equal(5, result.Transactions.Count + result.SkippedErrorCount);
    }
}
