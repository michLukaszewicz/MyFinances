using MyFinances.Api.Import;
using Xunit;

namespace MyFinances.Api.Tests;

public class MBankCsvParserTests
{
    private static string FixturePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "mbank-sample-redacted.csv");
    private static string DmyFixturePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "mbank-sample-redacted-dmy.csv");
    private static string Utf8RemojibakeFixturePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "mbank-sample-utf8-remojibake.csv");
    private static string CommaWrappedFixturePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "mbank-sample-comma-wrapped-utf8-remojibake.csv");

    private static FileStream OpenFixture() => File.OpenRead(FixturePath);
    private static FileStream OpenDmyFixture() => File.OpenRead(DmyFixturePath);
    private static FileStream OpenUtf8RemojibakeFixture() => File.OpenRead(Utf8RemojibakeFixturePath);
    private static FileStream OpenCommaWrappedFixture() => File.OpenRead(CommaWrappedFixturePath);

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

    [Fact]
    public void Parse_ExtractsExactlyFourValidTransactions_ForDmyDateFormatFixture()
    {
        // Some mBank exports (e.g. after being opened/resaved in Excel) use dd.MM.yyyy
        // instead of yyyy-MM-dd for Data księgowania/Data operacji.
        var parser = new MBankCsvParser();
        using var stream = OpenDmyFixture();

        var result = parser.Parse(stream);

        Assert.Equal(4, result.Transactions.Count);
        Assert.Equal(1, result.SkippedErrorCount);
    }

    [Fact]
    public void CanParse_ReturnsTrue_ForUtf8RemojibakeFixture()
    {
        // Some exports have been round-tripped through a tool that decoded the original cp1250
        // bytes as Windows-1252 and re-saved as UTF-8, corrupting every diacritic in the file —
        // including the one in the "#Data księgowania" header CanParse matches on.
        var parser = new MBankCsvParser();
        using var stream = OpenUtf8RemojibakeFixture();

        Assert.True(parser.CanParse(stream));
    }

    [Fact]
    public void Parse_RecoversCorrectPolishText_FromUtf8RemojibakeFixture()
    {
        var parser = new MBankCsvParser();
        using var stream = OpenUtf8RemojibakeFixture();

        var result = parser.Parse(stream);

        Assert.Equal(2, result.Transactions.Count);
        Assert.Contains(result.Transactions, t => t.Description == "NA JEDZENIE" && t.Amount == -500.00m);
        Assert.Contains(result.Transactions, t => t.Description == "Śklep żabka" && t.Amount == -120.00m);
    }

    [Fact]
    public void Parse_UnwrapsCommaPaddedRows_FromSpreadsheetResavedExport()
    {
        // Some exports have also been re-saved through a spreadsheet tool that wraps each real
        // ';'-row in an outer ','-quoted cell (doubling its internal quotes) plus trailing empty
        // ',' padding, on top of the UTF-8 remojibake. Both corruptions must be undone together.
        var parser = new MBankCsvParser();
        using var stream = OpenCommaWrappedFixture();

        var result = parser.Parse(stream);

        Assert.Equal(2, result.Transactions.Count);
        Assert.Contains(result.Transactions, t => t.Date == new DateOnly(2026, 8, 1) && t.Description == "NA JEDZENIE" && t.Amount == -500.00m);
        Assert.Contains(result.Transactions, t => t.Date == new DateOnly(2026, 8, 5) && t.Description == "Śklep żabka" && t.Amount == -120.00m);
    }
}
