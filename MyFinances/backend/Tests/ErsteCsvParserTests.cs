using System.Text;
using MyFinances.Api.Import;
using Xunit;

namespace MyFinances.Api.Tests;

public class ErsteCsvParserTests
{
    private static string FixturePath(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private static FileStream OpenSemicolonFixture() => File.OpenRead(FixturePath("erste-sample-redacted.csv"));
    private static FileStream OpenTabFixture() => File.OpenRead(FixturePath("erste-sample-redacted-tab.csv"));
    private static FileStream OpenPipeFixture() => File.OpenRead(FixturePath("erste-sample-redacted-pipe.csv"));
    private static FileStream OpenCommaFixture() => File.OpenRead(FixturePath("erste-sample-redacted-comma.csv"));
    private static FileStream OpenMBankFixture() => File.OpenRead(FixturePath("mbank-sample-redacted.csv"));
    private static FileStream OpenMBankRemojibakeFixture() => File.OpenRead(FixturePath("mbank-sample-utf8-remojibake.csv"));

    // Edits the semicolon fixture's text in memory and hands it back as a stream.
    private static MemoryStream SemicolonFixtureWith(Func<string, string> edit)
    {
        var text = File.ReadAllText(FixturePath("erste-sample-redacted.csv"), new UTF8Encoding(false));
        return new MemoryStream(Encoding.UTF8.GetBytes(edit(text)));
    }

    [Fact]
    public void CanParse_ReturnsTrue_ForEveryDelimiterVariantFixture()
    {
        var parser = new ErsteCsvParser();

        using var semicolon = OpenSemicolonFixture();
        using var tab = OpenTabFixture();
        using var pipe = OpenPipeFixture();
        using var comma = OpenCommaFixture();

        Assert.True(parser.CanParse(semicolon));
        Assert.True(parser.CanParse(tab));
        Assert.True(parser.CanParse(pipe));
        Assert.True(parser.CanParse(comma));
    }

    [Fact]
    public void CanParse_RestoresStreamPosition()
    {
        var parser = new ErsteCsvParser();
        using var stream = OpenSemicolonFixture();

        parser.CanParse(stream);

        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void CanParse_ReturnsFalseWithoutThrowing_ForMBankFixtures()
    {
        var parser = new ErsteCsvParser();

        using var cp1250 = OpenMBankFixture();
        using var remojibake = OpenMBankRemojibakeFixture();

        Assert.False(parser.CanParse(cp1250));
        Assert.False(parser.CanParse(remojibake));
    }

    [Fact]
    public void MBankCanParse_ReturnsFalse_ForErsteFixtures()
    {
        var mbank = new MBankCsvParser();

        using var semicolon = OpenSemicolonFixture();
        using var tab = OpenTabFixture();
        using var pipe = OpenPipeFixture();
        using var comma = OpenCommaFixture();

        Assert.False(mbank.CanParse(semicolon));
        Assert.False(mbank.CanParse(tab));
        Assert.False(mbank.CanParse(pipe));
        Assert.False(mbank.CanParse(comma));
    }

    [Fact]
    public void Parse_ReturnsNothing_ForNonErsteFile()
    {
        var parser = new ErsteCsvParser();
        using var stream = OpenMBankFixture();

        var result = parser.Parse(stream);

        Assert.Empty(result.Transactions);
        Assert.Equal(0, result.SkippedErrorCount);
    }

    [Fact]
    public void Parse_ReturnsTwentyNineTransactionsAndNoSkips_ForSemicolonFixture()
    {
        var parser = new ErsteCsvParser();
        using var stream = OpenSemicolonFixture();

        var result = parser.Parse(stream);

        Assert.Equal(29, result.Transactions.Count);
        Assert.Equal(0, result.SkippedErrorCount);
        // The summary line (export date 2026-10-01) must not leak in as a transaction.
        Assert.DoesNotContain(result.Transactions, t => t.Date == new DateOnly(2026, 10, 1));
    }

    [Fact]
    public void Parse_UsesTransactionDateRatherThanBookingDate()
    {
        var parser = new ErsteCsvParser();
        using var stream = OpenSemicolonFixture();

        var result = parser.Parse(stream);

        // Booked 03-09-2026, paid 02-09-2026.
        Assert.Contains(result.Transactions, t => t.Date == new DateOnly(2026, 9, 2) && t.Amount == -39.95m);
        // Booked 06-09-2026, paid 04-09-2026.
        Assert.Contains(result.Transactions, t => t.Date == new DateOnly(2026, 9, 4) && t.Amount == -205.69m);
    }

    [Fact]
    public void Parse_ReadsSignedPolishDecimalAmounts()
    {
        var parser = new ErsteCsvParser();
        using var stream = OpenSemicolonFixture();

        var result = parser.Parse(stream);

        Assert.Contains(result.Transactions, t => t.Amount == -69.98m && t.Description.Contains("Allegro"));
        Assert.Contains(result.Transactions, t => t.Amount == 45.00m && t.Description.Contains("ZWROT PŁATNOŚCI KARTĄ 45.00"));
    }

    [Fact]
    public void Parse_ReturnsAllThreeIdenticalTransferRows()
    {
        var parser = new ErsteCsvParser();
        using var stream = OpenSemicolonFixture();

        var result = parser.Parse(stream);

        var transfers = result.Transactions
            .Where(t => t.Date == new DateOnly(2026, 9, 4) && t.Amount == 500.00m && t.Description == "PRZELEW ŚRODKÓW")
            .ToList();
        Assert.Equal(3, transfers.Count);
    }

    [Fact]
    public void Parse_PreservesPolishDiacriticsInDescription()
    {
        var parser = new ErsteCsvParser();
        using var stream = OpenSemicolonFixture();

        var result = parser.Parse(stream);

        Assert.Contains(result.Transactions, t => t.Description == "Wpłata końcówek na cel- zakup przy użyciu karty na kwotę 69.98 PLN (mnożnik x5)");
    }

    [Fact]
    public void Parse_ReturnsIdenticalTwentyOneTransactions_ForTabPipeAndCommaFixtures()
    {
        var parser = new ErsteCsvParser();

        using var tab = OpenTabFixture();
        using var pipe = OpenPipeFixture();
        using var comma = OpenCommaFixture();

        var tabResult = parser.Parse(tab);
        var pipeResult = parser.Parse(pipe);
        var commaResult = parser.Parse(comma);

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
        var parser = new ErsteCsvParser();
        using var stream = OpenCommaFixture();

        var result = parser.Parse(stream);

        // Quoted "-261,54" row, booked 01-09-2026, paid 31-08-2026.
        Assert.Contains(result.Transactions, t => t.Date == new DateOnly(2026, 8, 31) && t.Amount == -261.54m);
        // Quoted refund "4,99".
        Assert.Contains(result.Transactions, t => t.Amount == 4.99m);
    }

    [Fact]
    public void Parse_CountsMalformedAmountAsSkipped_AndKeepsOtherRows()
    {
        var parser = new ErsteCsvParser();
        using var stream = SemicolonFixtureWith(text => text.Replace(";-69,98;", ";not-a-number;"));

        var result = parser.Parse(stream);

        Assert.Equal(1, result.SkippedErrorCount);
        Assert.Equal(28, result.Transactions.Count);
    }

    [Fact]
    public void Parse_ReturnsNoTransactionsAndCountsDataRowsAsSkipped_ForNonPlnStatement()
    {
        var parser = new ErsteCsvParser();
        using var stream = SemicolonFixtureWith(text => text.Replace(";PLN;530,57;", ";EUR;530,57;"));

        var result = parser.Parse(stream);

        Assert.Empty(result.Transactions);
        Assert.Equal(29, result.SkippedErrorCount);
    }
}
