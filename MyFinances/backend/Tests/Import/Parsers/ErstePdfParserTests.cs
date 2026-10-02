using System.Text;
using System.Text.RegularExpressions;
using MyFinances.Api.Import;
using MyFinances.Api.Tests.Support;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace MyFinances.Api.Tests;

// Runs the parser over the synthetic Erste PDFs (committed fixtures and variants generated in
// memory by ErstePdfBuilder). The real statement contains personal data and is only used for a
// manual check outside the repository.
public class ErstePdfParserTests
{
    private const string OnePageFixture = "erste-pdf-sample-synthetic.pdf";
    private const string MultiPageFixture = "erste-pdf-sample-synthetic-multipage.pdf";

    // The checks, as the parser names them in its messages.
    private const string BalanceCheck = "the balances do not link up";
    private const string UnreadableRowCheck = "a table row could not be read";

    // Indexes into ErsteSampleData.OnePageRows (printed order, newest booking first).
    private const int RefundRow = 4;     // 2026-09-30, two operation lines, +29.99
    private const int ThousandRow = 9;   // 2026-09-29, +1 200,00
    private const int MiddleRow = 5;     // 2026-09-30, -142.35

    private static string FixturePath(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private static byte[] ReadFixture(string name) => File.ReadAllBytes(FixturePath(name));

    private static IReadOnlyList<ErstePdfRow> OnePage => ErsteSampleData.OnePageRows;

    private static IReadOnlyList<ErstePdfRow> MultiPage => ErsteSampleData.MultiPageRows;

    private static ParseResult Parse(byte[] pdf, int maxPages = ErstePdfParser.DefaultMaxPages) =>
        new ErstePdfParser(maxPages).Parse(new MemoryStream(pdf));

    private static bool CanParse(byte[] bytes, int maxPages = ErstePdfParser.DefaultMaxPages) =>
        new ErstePdfParser(maxPages).CanParse(new MemoryStream(bytes));

    // What a faithful parse of the rows returns: operation date, signed amount, and the operation
    // lines joined with single spaces, in printed order.
    private static List<NormalizedTransaction> Expected(IEnumerable<ErstePdfRow> rows) =>
        rows.Select(r => new NormalizedTransaction(r.OperationDate, string.Join(" ", r.OperationLines), r.Amount)).ToList();

    private static byte[] BuildOnePagePdf(IReadOnlyList<ErstePdfRow> rows) =>
        ErstePdfBuilder.Build(ErsteSampleData.Header, rows, ErsteSampleData.OnePageLayout);

    private static byte[] BuildMultiPagePdf(IReadOnlyList<ErstePdfRow> rows) =>
        ErstePdfBuilder.Build(ErsteSampleData.Header, rows, ErsteSampleData.MultiPageLayout);

    private static List<ErstePdfRow> WithRow(IReadOnlyList<ErstePdfRow> rows, int index, ErstePdfRow replacement)
    {
        var copy = rows.ToList();
        copy[index] = replacement;
        return copy;
    }

    private static List<ErstePdfRow> Without(IReadOnlyList<ErstePdfRow> rows, ErstePdfRow removed) =>
        rows.Where(r => !ReferenceEquals(r, removed)).ToList();

    // A one-page PDF that is not a bank statement, or that carries only some of the recognition anchors.
    private static byte[] BuildTextPdf(params string[] lines)
    {
        using var builder = new PdfDocumentBuilder();
        var font = builder.AddTrueTypeFont(File.ReadAllBytes(FixturePath(Path.Combine("fonts", "NotoSans-Regular.ttf"))));
        var page = builder.AddPage(PageSize.A4);
        var y = 780.0;
        foreach (var line in lines.Where(l => l.Length > 0))
        {
            page.AddText(line, 12, new PdfPoint(40, y), font);
            y -= 20;
        }

        return builder.Build();
    }

    private static byte[] PdfMagicFollowedByGarbage()
    {
        var garbage = new byte[400];
        new Random(1).NextBytes(garbage);
        return [.. Encoding.ASCII.GetBytes("%PDF-1.7\n"), .. garbage];
    }

    // The message must have exactly this shape: the failing check, the page, the row's operation
    // date when readable, and a closing sentence without digits — so no amount, description or name fits in.
    private static StatementIntegrityException AssertRejected(byte[] pdf, string check, int? page = null, string? operationDate = null, bool dateReadable = true)
    {
        var exception = Assert.Throws<StatementIntegrityException>(() => Parse(pdf));

        var match = Regex.Match(
            exception.Message,
            @"^Erste statement rejected: (?<check>[^()]+) \(page (?<page>\d+)(, transaction date (?<date>\d{2}\.\d{2}\.\d{4}))?\)\. [^0-9]*$");
        Assert.True(match.Success, exception.Message);
        Assert.Equal(check, match.Groups["check"].Value);
        if (page is { } expectedPage)
        {
            Assert.Equal(expectedPage.ToString(), match.Groups["page"].Value);
        }

        if (operationDate is not null || !dateReadable)
        {
            Assert.Equal(operationDate ?? string.Empty, match.Groups["date"].Value);
        }

        foreach (var personal in new[] { ErsteSampleData.Header.AccountNumber, "Testow", "Próbn", "Przykład", "Market", "Sklep", "PLN" })
        {
            Assert.DoesNotContain(personal, exception.Message);
        }

        return exception;
    }

    [Fact]
    public void Parser_IdentifiesItselfAsTheErstePdfParser()
    {
        // Arrange
        var parser = new ErstePdfParser();

        // Assert
        Assert.Equal("Erste", parser.BankName);
        Assert.Equal(StatementFormat.Pdf, parser.Format);
    }

    [Theory]
    [InlineData(OnePageFixture)]
    [InlineData(MultiPageFixture)]
    public void CanParse_ReturnsTrue_ForBothSyntheticFixtures(string fileName)
    {
        // Act & Assert
        Assert.True(CanParse(ReadFixture(fileName)));
    }

    [Theory]
    [InlineData("erste-sample-redacted.csv")]
    [InlineData("erste-sample-redacted-tab.csv")]
    [InlineData("erste-sample-redacted-pipe.csv")]
    [InlineData("erste-sample-redacted-comma.csv")]
    public void CanParse_ReturnsFalseWithoutThrowing_ForErsteCsvFixtures(string fileName)
    {
        // Arrange
        using var stream = File.OpenRead(FixturePath(fileName));

        // Act & Assert
        Assert.False(new ErstePdfParser().CanParse(stream));
    }

    [Theory]
    [InlineData("mbank-pdf-sample-synthetic.pdf")]
    [InlineData("mbank-pdf-sample-synthetic-multipage.pdf")]
    [InlineData("velobank-sample-synthetic.pdf")]
    [InlineData("velobank-sample-synthetic-multipage.pdf")]
    public void CanParse_ReturnsFalse_ForMBankAndVeloBankPdfFixtures(string fileName)
    {
        // Act & Assert
        Assert.False(CanParse(ReadFixture(fileName)));
    }

    [Theory]
    [InlineData(OnePageFixture)]
    [InlineData(MultiPageFixture)]
    public void MBankAndVeloBankParsers_ReturnFalse_ForErstePdfFixtures(string fileName)
    {
        // Act & Assert
        Assert.False(new MBankPdfParser().CanParse(new MemoryStream(ReadFixture(fileName))));
        Assert.False(new VeloBankPdfParser().CanParse(new MemoryStream(ReadFixture(fileName))));
    }

    [Fact]
    public void CanParse_ReturnsFalse_ForPdfThatIsNotAnErsteStatement()
    {
        // Arrange
        var pdf = BuildTextPdf("Historia rachunku", "VeloBank S.A.", "DATA TRANSAKCJI DATA KSIĘGOWANIA OPIS KWOTA SALDO");

        // Act & Assert
        Assert.False(CanParse(pdf));
    }

    // Every recognition word is on the page, but there is no table header block: not an Erste statement.
    [Fact]
    public void CanParse_ReturnsFalse_ForPdfWithAllTheWordsButNoTableHeaderBlock()
    {
        // Arrange
        var pdf = BuildTextPdf("Konto: 00 0000 0000 0000", "Lista transakcji", "Data operacji", "Operacja", "Kwota", "Saldo");

        // Act & Assert
        Assert.False(CanParse(pdf));
        Assert.Empty(Parse(pdf).Transactions);
    }

    [Fact]
    public void CanParse_ReturnsFalse_WhenTheTableHeaderBlockIsMissingFromPageOne()
    {
        // Arrange
        var layout = ErsteSampleData.OnePageLayout with { OmitHeaderOnPage = 1 };

        // Act & Assert
        Assert.False(CanParse(ErstePdfBuilder.Build(ErsteSampleData.Header, OnePage, layout)));
    }

    [Fact]
    public void CanParse_RequiresTheTitleAndTheAccountLine()
    {
        // Act & Assert
        Assert.False(CanParse(ErstePdfBuilder.Build(ErsteSampleData.Header, OnePage, ErsteSampleData.OnePageLayout with { OmitTitle = true })));
        Assert.False(CanParse(ErstePdfBuilder.Build(ErsteSampleData.Header, OnePage, ErsteSampleData.OnePageLayout with { OmitAccountLine = true })));
    }

    [Fact]
    public void CanParse_ReturnsFalse_ForPdfMagicFollowedByGarbage()
    {
        // Act & Assert
        Assert.False(CanParse(PdfMagicFollowedByGarbage()));
    }

    [Fact]
    public void CanParse_ReturnsFalse_ForEmptyStream()
    {
        // Act & Assert
        Assert.False(CanParse([]));
    }

    [Fact]
    public void CanParse_ReturnsFalse_WhenThePdfHasMoreThanMaxPages()
    {
        // Arrange
        var threePages = ReadFixture(MultiPageFixture);

        // Act & Assert
        Assert.False(CanParse(threePages, maxPages: 2));
        Assert.True(CanParse(threePages, maxPages: 3));
    }

    [Fact]
    public void CanParse_RestoresStreamPosition()
    {
        // Arrange
        var parser = new ErstePdfParser();
        using var stream = new MemoryStream(ReadFixture(OnePageFixture));

        // Act & Assert
        Assert.True(parser.CanParse(stream));

        // Assert
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void CanParse_ReadsFromTheCurrentPosition_AndRestoresIt()
    {
        // Arrange
        var parser = new ErstePdfParser();
        using var stream = new MemoryStream([.. new byte[7], .. ReadFixture(OnePageFixture)]);
        stream.Position = 7;

        // Act & Assert
        Assert.True(parser.CanParse(stream));

        // Assert
        Assert.Equal(7, stream.Position);
    }

    [Fact]
    public void Parse_ReturnsTwelveRowsInPrintedOrder_ForOnePageFixture()
    {
        // Act
        var result = Parse(ReadFixture(OnePageFixture));

        // Assert
        Assert.Equal(ErsteSampleData.OnePageRowCount, result.Transactions.Count);
        Assert.Equal(0, result.SkippedErrorCount);
        Assert.Equal(Expected(OnePage), result.Transactions);
    }

    [Fact]
    public void Parse_ReadsOperationDates_IncludingAnOctoberOne()
    {
        // Act
        var result = Parse(ReadFixture(OnePageFixture));

        // Assert
        Assert.Equal(new DateOnly(2026, 10, 1), result.Transactions[0].Date);
        Assert.Contains(result.Transactions, t => t.Date.Month == 10);
        Assert.Contains(result.Transactions, t => t.Date == new DateOnly(2026, 9, 28));
        // Operated the day before it was booked.
        Assert.Contains(result.Transactions, t => t.Date == new DateOnly(2026, 9, 30) && t.Amount == -12.50m);
    }

    [Fact]
    public void Parse_ReadsSignedAmounts_IncludingOnesOfAThousandOrMore()
    {
        // Act
        var result = Parse(ReadFixture(OnePageFixture));

        // Assert
        Assert.Contains(result.Transactions, t => t.Amount == 1200.00m && t.Description.StartsWith("Biuro Przykładowe"));
        Assert.Contains(result.Transactions, t => t.Amount == -142.35m);
        Assert.Contains(result.Transactions, t => t.Amount == 29.99m);
        Assert.Equal(OnePage[ThousandRow].Amount, result.Transactions[ThousandRow].Amount);
        // A balance of 1 000+ is read in one piece: otherwise the linkage check would have rejected the file.
        Assert.Contains(OnePage, r => r.Balance >= 1000m);
    }

    [Fact]
    public void Parse_JoinsATwoLineOperationCellWithASingleSpace()
    {
        // Arrange
        var refund = OnePage[RefundRow];
        Assert.Equal(2, refund.OperationLines.Count);

        // Act
        var result = Parse(ReadFixture(OnePageFixture));

        // Assert
        var description = result.Transactions[RefundRow].Description;
        Assert.Equal(
            "DOP. MC ZWROT KARTĄ 29.99 PLN Sklep Testowy 12 Testowo",
            description);
        Assert.DoesNotContain("  ", description);
        Assert.DoesNotContain("księgowania", description);
    }

    [Fact]
    public void Parse_KeepsSameDayRowsInPrintedOrder_AndAllRowsOfAnIdenticalKeyGroup()
    {
        // Act
        var result = Parse(ReadFixture(OnePageFixture));

        // Assert
        // Rows 0-2 share 2026-10-01 but their printed order does not follow the balance chain.
        Assert.Equal(
            new[] { "Kawiarnia Testowa Testowo", "Piekarnia Test Testowo", "Sklep Testowy 12 Testowo" },
            result.Transactions.Take(3).Select(t => t.Description).ToArray());
        Assert.Equal(3, result.Transactions.Count(t => t.Description == "Sklep Internetowy Testowy" && t.Amount == -19.99m));
    }

    [Fact]
    public void Parse_TakesNoHeaderFooterOrLabelTextIntoRows()
    {
        foreach (var fixture in new[] { OnePageFixture, MultiPageFixture })
        {
            // Act
            var result = Parse(ReadFixture(fixture));

            // Assert
            Assert.All(result.Transactions, t =>
            {
                Assert.DoesNotContain("księgowania", t.Description);
                Assert.DoesNotContain("Strona", t.Description);
                Assert.DoesNotContain("Dokument", t.Description);
                Assert.DoesNotContain("Saldo", t.Description);
                Assert.DoesNotContain("Kwota", t.Description);
            });
        }
    }

    [Fact]
    public void Parse_ReturnsFortyRows_ForMultiPageFixture()
    {
        // Act
        var result = Parse(ReadFixture(MultiPageFixture));

        // Assert
        Assert.Equal(ErsteSampleData.MultiPageRowCount, result.Transactions.Count);
        Assert.Equal(0, result.SkippedErrorCount);
        Assert.Equal(Expected(MultiPage), result.Transactions);
    }

    [Theory]
    [InlineData(10, 10)]
    [InlineData(12, 14)]
    [InlineData(20, 20)]
    [InlineData(6, 17)]
    public void Parse_ReturnsTheSameRows_WhateverThePageBreaks(int firstPageRows, int rowsPerPage)
    {
        // Arrange
        var layout = new ErstePdfLayout { FirstPageRows = firstPageRows, RowsPerPage = rowsPerPage };

        // Act
        var result = Parse(ErstePdfBuilder.Build(ErsteSampleData.Header, MultiPage, layout));

        // Assert
        Assert.Equal(Expected(MultiPage), result.Transactions);
    }

    [Fact]
    public void Parse_AssemblesAmountsAndBalancesPrintedWithAThousandsSpace()
    {
        // Arrange
        // The widest values that still fit the 93 pt amount column.
        var row = OnePage[ThousandRow] with { Amount = 123_456.78m, Balance = 123_461.78m };
        var pdf = ErstePdfBuilder.Build(ErsteSampleData.Header, [row]);

        // Act
        var result = Parse(pdf);

        // Assert
        Assert.Equal(123_456.78m, Assert.Single(result.Transactions).Amount);
    }

    [Fact]
    public void Parse_ReadsAStatementWithoutAnyRow()
    {
        // Arrange
        var pdf = ErstePdfBuilder.Build(ErsteSampleData.Header, []);

        // Act
        var result = Parse(pdf);

        // Assert
        Assert.Empty(result.Transactions);
        Assert.Equal(0, result.SkippedErrorCount);
    }

    [Fact]
    public void Parse_Rejects_ATamperedBalance()
    {
        // Arrange
        var tampered = OnePage[MiddleRow] with { Balance = OnePage[MiddleRow].Balance + 5m };

        // Act & Assert
        var exception = AssertRejected(BuildOnePagePdf(WithRow(OnePage, MiddleRow, tampered)), BalanceCheck, page: 1);

        // Assert
        Assert.DoesNotContain("5,00", exception.Message);
    }

    [Fact]
    public void Parse_Rejects_ATamperedBalanceOnALaterPage()
    {
        // Arrange
        var tampered = MultiPage[20] with { Balance = MultiPage[20].Balance - 1m };

        // Act & Assert
        AssertRejected(BuildMultiPagePdf(WithRow(MultiPage, 20, tampered)), BalanceCheck);
    }

    [Fact]
    public void Parse_Rejects_ADeletedMiddleRow()
    {
        // Act & Assert
        AssertRejected(BuildMultiPagePdf(Without(MultiPage, MultiPage[20])), BalanceCheck);
    }

    // The documented blind spot: the oldest row's before-balance and the newest row's after-balance
    // are the two values that may stay unmatched, so a missing row at either end cannot be seen.
    [Fact]
    public void Parse_Accepts_ADeletedNewestRow()
    {
        // Arrange
        var newest = MultiPage.Single(r => r.Balance == ErsteSampleData.NewestBalance);

        // Act
        var result = Parse(BuildMultiPagePdf(Without(MultiPage, newest)));

        // Assert
        Assert.Equal(ErsteSampleData.MultiPageRowCount - 1, result.Transactions.Count);
    }

    [Fact]
    public void Parse_Accepts_ADeletedOldestRow()
    {
        // Arrange
        var oldest = ErsteSampleData.MultiPageRowsInBookingOrder[0];

        // Act
        var result = Parse(BuildMultiPagePdf(Without(MultiPage, oldest)));

        // Assert
        Assert.Equal(ErsteSampleData.MultiPageRowCount - 1, result.Transactions.Count);
    }

    [Fact]
    public void Parse_Rejects_AnUnknownMonthToken_WithoutADate()
    {
        // Arrange
        var unreadable = OnePage[MiddleRow] with { DateText = "28 xyz 2026" };

        // Act & Assert
        AssertRejected(BuildOnePagePdf(WithRow(OnePage, MiddleRow, unreadable)), UnreadableRowCheck, page: 1, dateReadable: false);
    }

    [Fact]
    public void Parse_Rejects_AnImpossibleDate()
    {
        // Arrange
        var unreadable = OnePage[MiddleRow] with { DateText = "31 wrz 2026" };

        // Act & Assert
        AssertRejected(BuildOnePagePdf(WithRow(OnePage, MiddleRow, unreadable)), UnreadableRowCheck, page: 1, dateReadable: false);
    }

    [Fact]
    public void Parse_Rejects_AnUnreadableAmountCell_WithTheRowsDate()
    {
        // Arrange
        var unreadable = OnePage[MiddleRow] with { AmountText = "n/a" };

        // Act & Assert
        AssertRejected(BuildOnePagePdf(WithRow(OnePage, MiddleRow, unreadable)), UnreadableRowCheck, page: 1, operationDate: "30.09.2026");
    }

    // Far beyond decimal range: must be an unreadable cell (422), not an OverflowException (500).
    [Theory]
    [InlineData("99999999999999999999999999999,99 PLN")]
    [InlineData("12345678901234567,89 PLN")]
    public void Parse_Rejects_AnAbsurdlyLargeAmountCell_AsUnreadable(string amountText)
    {
        // Arrange
        var unreadable = OnePage[MiddleRow] with { AmountText = amountText };

        // Act & Assert
        AssertRejected(BuildOnePagePdf(WithRow(OnePage, MiddleRow, unreadable)), UnreadableRowCheck, page: 1, operationDate: "30.09.2026");
    }

    [Fact]
    public void Parse_Rejects_AnUnreadableBalanceCell_WithTheRowsDate()
    {
        // Arrange
        var unreadable = OnePage[MiddleRow] with { BalanceText = "brak" };

        // Act & Assert
        AssertRejected(BuildOnePagePdf(WithRow(OnePage, MiddleRow, unreadable)), UnreadableRowCheck, page: 1, operationDate: "30.09.2026");
    }

    [Fact]
    public void Parse_Rejects_APageAfterTheFirstWithoutATableHeader()
    {
        // Arrange
        var layout = ErsteSampleData.MultiPageLayout with { OmitHeaderOnPage = 2 };

        // Act & Assert
        AssertRejected(ErstePdfBuilder.Build(ErsteSampleData.Header, MultiPage, layout), UnreadableRowCheck, page: 2, dateReadable: false);
    }

    [Fact]
    public void Parse_SkipsAForeignCurrencyRow_AndCountsIt()
    {
        // Arrange
        var eur = OnePage[MiddleRow] with { Currency = "EUR", Amount = -30.00m };

        // Act
        var result = Parse(BuildOnePagePdf(WithRow(OnePage, MiddleRow, eur)));

        // Assert
        Assert.Equal(1, result.SkippedErrorCount);
        Assert.Equal(ErsteSampleData.OnePageRowCount - 1, result.Transactions.Count);
        Assert.DoesNotContain(result.Transactions, t => t.Amount == -30.00m);
        Assert.Equal(Expected(Without(OnePage, OnePage[MiddleRow])), result.Transactions);
    }

    [Fact]
    public void Parse_DisablesTheBalanceCheck_ForAFileWithAForeignCurrencyRow()
    {
        // Arrange
        var eur = OnePage[MiddleRow] with { Currency = "EUR", Amount = -30.00m };
        var rows = WithRow(OnePage, MiddleRow, eur);
        // A tampered PLN balance would be rejected without the foreign row ...
        rows[ThousandRow] = rows[ThousandRow] with { Balance = rows[ThousandRow].Balance + 7m };
        Assert.Throws<StatementIntegrityException>(() => Parse(BuildOnePagePdf(WithRow(OnePage, ThousandRow, rows[ThousandRow]))));

        // Act
        // ... and passes with it, because the effect of the foreign row on the printed balance is unverified.
        var result = Parse(BuildOnePagePdf(rows));

        // Assert
        Assert.Equal(1, result.SkippedErrorCount);
    }

    [Fact]
    public void Parse_ReturnsNothing_ForPdfThatIsNotAnErsteStatement()
    {
        // Act
        var result = Parse(BuildTextPdf("Historia rachunku", "VeloBank S.A."));

        // Assert
        Assert.Empty(result.Transactions);
        Assert.Equal(0, result.SkippedErrorCount);
    }

    [Fact]
    public void Parse_ReturnsNothing_ForMBankAndVeloBankPdfFixtures()
    {
        foreach (var fixture in new[] { "mbank-pdf-sample-synthetic.pdf", "velobank-sample-synthetic.pdf" })
        {
            // Act
            var result = Parse(ReadFixture(fixture));

            // Assert
            Assert.Empty(result.Transactions);
            Assert.Equal(0, result.SkippedErrorCount);
        }
    }

    [Fact]
    public void Parse_ReturnsNothingWithoutThrowing_ForCorruptContent()
    {
        foreach (var bytes in new[] { PdfMagicFollowedByGarbage(), Array.Empty<byte>(), "not a pdf at all"u8.ToArray(), File.ReadAllBytes(FixturePath("erste-sample-redacted.csv")) })
        {
            // Act
            var result = Parse(bytes);

            // Assert
            Assert.Empty(result.Transactions);
            Assert.Equal(0, result.SkippedErrorCount);
        }
    }

    [Fact]
    public void Parse_ReturnsNothing_WhenThePdfHasMoreThanMaxPages()
    {
        // Act
        var result = Parse(ReadFixture(MultiPageFixture), maxPages: 2);

        // Assert
        Assert.Empty(result.Transactions);
        Assert.Equal(0, result.SkippedErrorCount);
    }

    [Fact]
    public void Parse_RestoresStreamPosition()
    {
        // Arrange
        var parser = new ErstePdfParser();
        using var stream = new MemoryStream([.. new byte[7], .. ReadFixture(OnePageFixture)]);
        stream.Position = 7;

        // Act
        var result = parser.Parse(stream);

        // Assert
        Assert.Equal(ErsteSampleData.OnePageRowCount, result.Transactions.Count);
        Assert.Equal(7, stream.Position);
    }

    [Fact]
    public void CanParseThenParse_WorksOnOneStream()
    {
        // Arrange
        var parser = new ErstePdfParser();
        using var stream = new MemoryStream(ReadFixture(MultiPageFixture));

        // Act & Assert
        Assert.True(parser.CanParse(stream));
        var result = parser.Parse(stream);

        // Assert
        Assert.Equal(ErsteSampleData.MultiPageRowCount, result.Transactions.Count);
    }
}
