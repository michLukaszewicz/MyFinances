using System.Text;
using MyFinances.Api.Import;
using MyFinances.Api.Tests.Support;
using Xunit;

namespace MyFinances.Api.Tests.Import.Parsers;

public class MBankCsvParserTests
{
    private readonly MBankCsvParser _parser = new();

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
        // Arrange
        using var stream = OpenFixture();

        // Act & Assert
        Assert.True(_parser.CanParse(stream));
    }

    [Fact]
    public void Parse_ExtractsExactlyFourValidTransactions_IncludingBothBlikRows()
    {
        // Arrange
        using var stream = OpenFixture();

        // Act
        var result = _parser.Parse(stream);

        // Assert
        Assert.Equal(4, result.Transactions.Count);

        var blikRows = result.Transactions
            .Where(t => t.Date == new DateOnly(2026, 8, 1) && t.Amount == -500.00m && t.Description == "NA JEDZENIE")
            .ToList();
        Assert.Equal(2, blikRows.Count);
    }

    [Fact]
    public void Parse_SkipsMalformedRow_WithoutAbortingRestOfFile()
    {
        // Arrange
        using var stream = OpenFixture();

        // Act
        var result = _parser.Parse(stream);

        // Assert
        Assert.Contains(result.Transactions, t => t.Date == new DateOnly(2026, 8, 5) && t.Amount == -120.00m);
    }

    [Fact]
    public void Parse_ReportsOneSkippedErrorRow_ForFixture()
    {
        // Arrange
        using var stream = OpenFixture();

        // Act
        var result = _parser.Parse(stream);

        // Assert
        Assert.Equal(1, result.SkippedErrorCount);
    }

    [Fact]
    public void Parse_ExcludesFooterRows_FromTransactionsAndErrorCount()
    {
        // Arrange
        using var stream = OpenFixture();

        // Act
        var result = _parser.Parse(stream);

        // Assert
        Assert.DoesNotContain(result.Transactions, t => t.Description.Contains("informacyjny", StringComparison.OrdinalIgnoreCase));
        // 4 valid + 1 skipped-error == 5 data rows accounted for; footer rows contribute to neither.
        Assert.Equal(5, result.Transactions.Count + result.SkippedErrorCount);
    }

    [Fact]
    public void Parse_ExtractsExactlyFourValidTransactions_ForDmyDateFormatFixture()
    {
        // Arrange
        // Some mBank exports (e.g. after being opened/resaved in Excel) use dd.MM.yyyy
        // instead of yyyy-MM-dd for Data księgowania/Data operacji.
        using var stream = OpenDmyFixture();

        // Act
        var result = _parser.Parse(stream);

        // Assert
        Assert.Equal(4, result.Transactions.Count);
        Assert.Equal(1, result.SkippedErrorCount);
        // By value, so a day/month swap (e.g. 02.08 read as 8 February) cannot pass.
        Assert.Equal(
            [
                new NormalizedTransaction(new DateOnly(2026, 8, 1), "NA JEDZENIE", -500.00m),
                new NormalizedTransaction(new DateOnly(2026, 8, 1), "NA JEDZENIE", -500.00m),
                new NormalizedTransaction(new DateOnly(2026, 8, 2), "ZWROT", 250.50m),
                new NormalizedTransaction(new DateOnly(2026, 8, 5), "SKLEP SPOZYWCZY DATA TRANSAKCJI: 2026-08-05", -120.00m),
            ],
            result.Transactions);
    }

    [Fact]
    public void Parse_ReadsKwotaWithRegularSpaceThousands_AsExactValue()
    {
        // Arrange
        using var stream = BuildWithRawKwota("1234,56", "1 234,56");

        // Act
        var result = _parser.Parse(stream);

        // Assert
        // Observed outcome: parsed to the correct value (pl-PL accepts a regular space as group separator).
        var transaction = Assert.Single(result.Transactions);
        Assert.Equal(new NormalizedTransaction(new DateOnly(2026, 8, 1), "ROW", 1234.56m), transaction);
        Assert.Equal(0, result.SkippedErrorCount);
    }

    [Fact]
    public void Parse_ReadsKwotaWithNbspThousands_AsExactValue()
    {
        // Arrange
        using var stream = BuildWithRawKwota("1234,56", "1 234,56");

        // Act
        var result = _parser.Parse(stream);

        // Assert
        // Observed outcome: parsed to the correct value (NBSP is the pl-PL group separator).
        var transaction = Assert.Single(result.Transactions);
        Assert.Equal(new NormalizedTransaction(new DateOnly(2026, 8, 1), "ROW", 1234.56m), transaction);
        Assert.Equal(0, result.SkippedErrorCount);
    }

    [Fact]
    public void Parse_StopsSilentlyAtMidFileRowWithUnparseableDate_KnownLimitation()
    {
        // Arrange
        var bytes = MBankCsvBuilder.Build(
        [
            new MBankCsvRow(new DateOnly(2026, 8, 1), "FIRST", -10.00m),
            new MBankCsvRow(new DateOnly(2026, 8, 2), "BAD DATE", -20.00m),
            new MBankCsvRow(new DateOnly(2026, 8, 3), "AFTER", -30.00m),
        ]);
        var cp1250 = Encoding.GetEncoding(1250);
        var text = cp1250.GetString(bytes).Replace("2026-08-02;2026-08-02;", "1.8.2026;1.8.2026;");
        using var stream = new MemoryStream(cp1250.GetBytes(text));

        // Act
        var result = _parser.Parse(stream);

        // Assert
        // Known limitation: the unparseable date is treated as the footer, so every later row is
        // dropped without being counted as skipped.
        var transaction = Assert.Single(result.Transactions);
        Assert.Equal(new NormalizedTransaction(new DateOnly(2026, 8, 1), "FIRST", -10.00m), transaction);
        Assert.Equal(0, result.SkippedErrorCount);
    }

    // One row (2026-08-01, title "ROW") whose Kwota text is rewritten after the builder ran, since
    // the builder only emits plain "F2" amounts.
    private static MemoryStream BuildWithRawKwota(string builderAmount, string rawKwota)
    {
        var bytes = MBankCsvBuilder.Build([new MBankCsvRow(new DateOnly(2026, 8, 1), "ROW", 1234.56m)]);
        var cp1250 = Encoding.GetEncoding(1250);
        var text = cp1250.GetString(bytes).Replace($";{builderAmount};", $";{rawKwota};");
        return new MemoryStream(cp1250.GetBytes(text));
    }

    [Fact]
    public void CanParse_ReturnsTrue_ForUtf8RemojibakeFixture()
    {
        // Arrange
        // Some exports have been round-tripped through a tool that decoded the original cp1250
        // bytes as Windows-1252 and re-saved as UTF-8, corrupting every diacritic in the file —
        // including the one in the "#Data księgowania" header CanParse matches on.
        using var stream = OpenUtf8RemojibakeFixture();

        // Act & Assert
        Assert.True(_parser.CanParse(stream));
    }

    [Fact]
    public void Parse_RecoversCorrectPolishText_FromUtf8RemojibakeFixture()
    {
        // Arrange
        using var stream = OpenUtf8RemojibakeFixture();

        // Act
        var result = _parser.Parse(stream);

        // Assert
        Assert.Equal(2, result.Transactions.Count);
        Assert.Contains(result.Transactions, t => t.Description == "NA JEDZENIE" && t.Amount == -500.00m);
        Assert.Contains(result.Transactions, t => t.Description == "Śklep żabka" && t.Amount == -120.00m);
    }

    [Fact]
    public void Parse_UnwrapsCommaPaddedRows_FromSpreadsheetResavedExport()
    {
        // Arrange
        // Some exports have also been re-saved through a spreadsheet tool that wraps each real
        // ';'-row in an outer ','-quoted cell (doubling its internal quotes) plus trailing empty
        // ',' padding, on top of the UTF-8 remojibake. Both corruptions must be undone together.
        using var stream = OpenCommaWrappedFixture();

        // Act
        var result = _parser.Parse(stream);

        // Assert
        Assert.Equal(2, result.Transactions.Count);
        Assert.Contains(result.Transactions, t => t.Date == new DateOnly(2026, 8, 1) && t.Description == "NA JEDZENIE" && t.Amount == -500.00m);
        Assert.Contains(result.Transactions, t => t.Date == new DateOnly(2026, 8, 5) && t.Description == "Śklep żabka" && t.Amount == -120.00m);
    }
}
