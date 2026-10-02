using System.Text;
using MyFinances.Api.Import;
using Xunit;

namespace MyFinances.Api.Tests.Import.Parsers;

public class ErsteCsvParserTests
{
    private readonly ErsteCsvParser _parser = new();

    private static string FixturePath(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private static FileStream OpenSemicolonFixture() => File.OpenRead(FixturePath("erste-sample-redacted.csv"));
    private static FileStream OpenTabFixture() => File.OpenRead(FixturePath("erste-sample-redacted-tab.csv"));
    private static FileStream OpenPipeFixture() => File.OpenRead(FixturePath("erste-sample-redacted-pipe.csv"));
    private static FileStream OpenCommaFixture() => File.OpenRead(FixturePath("erste-sample-redacted-comma.csv"));
    private static FileStream OpenMBankFixture() => File.OpenRead(FixturePath("mbank-sample-redacted.csv"));
    private static FileStream OpenMBankRemojibakeFixture() => File.OpenRead(FixturePath("mbank-sample-utf8-remojibake.csv"));

    // Edits the semicolon fixture's text in memory and hands it back as a stream.
    private static MemoryStream SemicolonFixtureWith(Func<string, string> edit) =>
        FixtureWith("erste-sample-redacted.csv", edit);

    private static MemoryStream FixtureWith(string fixtureName, Func<string, string> edit)
    {
        var text = File.ReadAllText(FixturePath(fixtureName), new UTF8Encoding(false));
        return new MemoryStream(Encoding.UTF8.GetBytes(edit(text)));
    }

    [Fact]
    public void CanParse_ReturnsTrue_ForEveryDelimiterVariantFixture()
    {
        // Arrange
        using var semicolon = OpenSemicolonFixture();
        using var tab = OpenTabFixture();
        using var pipe = OpenPipeFixture();
        using var comma = OpenCommaFixture();

        // Act & Assert
        Assert.True(_parser.CanParse(semicolon));
        Assert.True(_parser.CanParse(tab));
        Assert.True(_parser.CanParse(pipe));
        Assert.True(_parser.CanParse(comma));
    }

    [Fact]
    public void CanParse_RestoresStreamPosition()
    {
        // Arrange
        using var stream = OpenSemicolonFixture();

        // Act
        _parser.CanParse(stream);

        // Assert
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void CanParse_ReturnsFalseWithoutThrowing_ForMBankFixtures()
    {
        // Arrange
        using var cp1250 = OpenMBankFixture();
        using var remojibake = OpenMBankRemojibakeFixture();

        // Act & Assert
        Assert.False(_parser.CanParse(cp1250));
        Assert.False(_parser.CanParse(remojibake));
    }

    [Fact]
    public void MBankCanParse_ReturnsFalse_ForErsteFixtures()
    {
        // Arrange
        var mbank = new MBankCsvParser();

        using var semicolon = OpenSemicolonFixture();
        using var tab = OpenTabFixture();
        using var pipe = OpenPipeFixture();
        using var comma = OpenCommaFixture();

        // Act & Assert
        Assert.False(mbank.CanParse(semicolon));
        Assert.False(mbank.CanParse(tab));
        Assert.False(mbank.CanParse(pipe));
        Assert.False(mbank.CanParse(comma));
    }

    [Fact]
    public void Parse_ReturnsNothing_ForNonErsteFile()
    {
        // Arrange
        using var stream = OpenMBankFixture();

        // Act
        var result = _parser.Parse(stream);

        // Assert
        Assert.Empty(result.Transactions);
        Assert.Equal(0, result.SkippedErrorCount);
    }

    [Fact]
    public void Parse_ReturnsTwentyNineTransactionsAndNoSkips_ForSemicolonFixture()
    {
        // Arrange
        using var stream = OpenSemicolonFixture();

        // Act
        var result = _parser.Parse(stream);

        // Assert
        Assert.Equal(29, result.Transactions.Count);
        Assert.Equal(0, result.SkippedErrorCount);
        // The summary line (export date 2026-10-01) must not leak in as a transaction.
        Assert.DoesNotContain(result.Transactions, t => t.Date == new DateOnly(2026, 10, 1));
    }

    [Fact]
    public void Parse_UsesTransactionDateRatherThanBookingDate()
    {
        // Arrange
        using var stream = OpenSemicolonFixture();

        // Act
        var result = _parser.Parse(stream);

        // Assert
        // Booked 03-09-2026, paid 02-09-2026.
        Assert.Contains(result.Transactions, t => t.Date == new DateOnly(2026, 9, 2) && t.Amount == -39.95m);
        // Booked 06-09-2026, paid 04-09-2026.
        Assert.Contains(result.Transactions, t => t.Date == new DateOnly(2026, 9, 4) && t.Amount == -205.69m);
    }

    [Fact]
    public void Parse_ReadsSignedPolishDecimalAmounts()
    {
        // Arrange
        using var stream = OpenSemicolonFixture();

        // Act
        var result = _parser.Parse(stream);

        // Assert
        Assert.Contains(result.Transactions, t => t.Amount == -69.98m && t.Description.Contains("Allegro"));
        Assert.Contains(result.Transactions, t => t.Amount == 45.00m && t.Description.Contains("ZWROT PŁATNOŚCI KARTĄ 45.00"));
    }

    [Fact]
    public void Parse_ReturnsAllThreeIdenticalTransferRows()
    {
        // Arrange
        using var stream = OpenSemicolonFixture();

        // Act
        var result = _parser.Parse(stream);

        // Assert
        var transfers = result.Transactions
            .Where(t => t.Date == new DateOnly(2026, 9, 4) && t.Amount == 500.00m && t.Description == "PRZELEW ŚRODKÓW")
            .ToList();
        Assert.Equal(3, transfers.Count);
    }

    [Fact]
    public void Parse_PreservesPolishDiacriticsInDescription()
    {
        // Arrange
        using var stream = OpenSemicolonFixture();

        // Act
        var result = _parser.Parse(stream);

        // Assert
        Assert.Contains(result.Transactions, t => t.Description == "Wpłata końcówek na cel- zakup przy użyciu karty na kwotę 69.98 PLN (mnożnik x5)");
    }

    [Fact]
    public void Parse_ReturnsIdenticalTwentyOneTransactions_ForTabPipeAndCommaFixtures()
    {
        // Arrange
        using var tab = OpenTabFixture();
        using var pipe = OpenPipeFixture();
        using var comma = OpenCommaFixture();

        // Act
        var tabResult = _parser.Parse(tab);
        var pipeResult = _parser.Parse(pipe);
        var commaResult = _parser.Parse(comma);

        // Assert
        Assert.Equal(21, tabResult.Transactions.Count);
        Assert.Equal(0, tabResult.SkippedErrorCount);
        Assert.Equal(0, pipeResult.SkippedErrorCount);
        Assert.Equal(0, commaResult.SkippedErrorCount);
        Assert.Equal(tabResult.Transactions, pipeResult.Transactions);
        Assert.Equal(tabResult.Transactions, commaResult.Transactions);
    }

    [Fact]
    public void Parse_ReadsQuotedAmountsAndDates_FromCommaFixture()
    {
        // Arrange
        using var stream = OpenCommaFixture();

        // Act
        var result = _parser.Parse(stream);

        // Assert
        // Quoted "-261,54" row, booked 01-09-2026, paid 31-08-2026.
        Assert.Contains(result.Transactions, t => t.Date == new DateOnly(2026, 8, 31) && t.Amount == -261.54m);
        // Quoted refund "4,99".
        Assert.Contains(result.Transactions, t => t.Amount == 4.99m);
    }

    [Fact]
    public void Parse_CountsMalformedAmountAsSkipped_AndKeepsOtherRows()
    {
        // Arrange
        using var stream = SemicolonFixtureWith(text => text.Replace(";-69,98;", ";not-a-number;"));

        // Act
        var result = _parser.Parse(stream);

        // Assert
        Assert.Equal(1, result.SkippedErrorCount);
        Assert.Equal(28, result.Transactions.Count);
        // The corrupted row (paid 26-09-2026) is the one that is gone; its neighbours keep their literal values.
        Assert.DoesNotContain(result.Transactions, t => t.Date == new DateOnly(2026, 9, 26));
        Assert.Contains(result.Transactions, t => t.Date == new DateOnly(2026, 9, 23) && t.Amount == -10.00m);
        Assert.Contains(result.Transactions, t => t.Date == new DateOnly(2026, 9, 21) && t.Amount == 45.00m);
    }

    [Theory]
    [InlineData("erste-sample-redacted-tab.csv")]
    [InlineData("erste-sample-redacted-pipe.csv")]
    [InlineData("erste-sample-redacted-comma.csv")]
    public void Parse_ReadsLiteralDatesAndAmounts_ForEachNonSemicolonDelimiterVariant(string fixtureName)
    {
        // Arrange
        using var stream = File.OpenRead(FixturePath(fixtureName));

        // Act
        var result = _parser.Parse(stream);

        // Assert
        // Booked 01-09-2026, paid 31-08-2026.
        Assert.Contains(result.Transactions, t => t.Date == new DateOnly(2026, 8, 31) && t.Amount == -261.54m);
        // Booked 12-08-2026, paid 12-08-2026.
        Assert.Contains(result.Transactions, t => t.Date == new DateOnly(2026, 8, 12) && t.Amount == 500.00m);
        // Booked 27-08-2026, paid 26-08-2026 (refund).
        Assert.Contains(result.Transactions, t => t.Date == new DateOnly(2026, 8, 26) && t.Amount == 4.99m);
    }

    [Theory]
    [InlineData("erste-sample-redacted.csv", ";-69,98;", ";-1 069,98;", 2026, 9, 26, -1069.98)]
    [InlineData("erste-sample-redacted-tab.csv", "	-261,54	", "	-1 261,54	", 2026, 8, 31, -1261.54)]
    [InlineData("erste-sample-redacted-pipe.csv", "|-261,54|", "|-1 261,54|", 2026, 8, 31, -1261.54)]
    [InlineData("erste-sample-redacted-comma.csv", "\"-261,54\"", "\"-1 261,54\"", 2026, 8, 31, -1261.54)]
    public void Parse_ReadsThousandsSeparatedAmount_AsExactValue(
        string fixtureName, string original, string replacement, int year, int month, int day, double expectedAmount)
    {
        // Arrange
        using var stream = FixtureWith(fixtureName, text => text.Replace(original, replacement));

        // Act
        var result = _parser.Parse(stream);

        // Assert
        Assert.Equal(0, result.SkippedErrorCount);
        Assert.Contains(result.Transactions, t => t.Date == new DateOnly(year, month, day) && t.Amount == (decimal)expectedAmount);
    }

    [Fact]
    public void Parse_ReturnsNoTransactionsAndCountsDataRowsAsSkipped_ForNonPlnStatement()
    {
        // Arrange
        using var stream = SemicolonFixtureWith(text => text.Replace(";PLN;530,57;", ";EUR;530,57;"));

        // Act
        var result = _parser.Parse(stream);

        // Assert
        Assert.Empty(result.Transactions);
        Assert.Equal(29, result.SkippedErrorCount);
    }

    [Fact]
    public void Parse_SkipsWhitespaceOnlyLeadingLine_BeforeTheSummary()
    {
        // Arrange
        using var stream = SemicolonFixtureWith(text => "   \n" + text);

        // Act
        Assert.True(_parser.CanParse(stream));

        var result = _parser.Parse(stream);

        // Assert
        Assert.Equal(29, result.Transactions.Count);
        Assert.Equal(0, result.SkippedErrorCount);
    }
}
