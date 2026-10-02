using System.Text;
using System.Text.RegularExpressions;
using MyFinances.Api.Import;
using MyFinances.Api.Tests.Support;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace MyFinances.Api.Tests;

// Runs the parser over the synthetic mBank PDFs (committed fixtures and variants generated in
// memory by MBankPdfBuilder). The real statement contains personal data and is only used for a
// manual check outside the repository.
public class MBankPdfParserTests
{
    private const string TwoPageFixture = "mbank-pdf-sample-synthetic.pdf";
    private const string MultiPageFixture = "mbank-pdf-sample-synthetic-multipage.pdf";

    // The checks, as the parser names them in its messages.
    private const string BalanceCheck = "the running balance does not add up";
    private const string ClosingBalanceCheck = "the closing balance differs from the balance of the last row";
    private const string SummaryCheck = "the turnover summary differs from the rows";
    private const string MissingOpeningCheck = "the opening balance is missing";
    private const string MissingClosingCheck = "the closing balance is missing";
    private const string MissingSummaryCheck = "the turnover summary is missing";
    private const string UnreadableRowCheck = "a table row could not be read";

    // Indexes into MBankSampleData.TwoPageRows (oldest first; page 1 holds rows 0-11).
    private const int PageOneRow = 5;             // 2026-09-13, a card payment
    private const int IncomingTransferRow = 12;   // 2026-09-17, five description lines, 3 200,00
    private const int SecondPageRow = 14;         // 2026-09-18, a BLIK payment

    private static string FixturePath(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private static byte[] ReadFixture(string name) => File.ReadAllBytes(FixturePath(name));

    private static IReadOnlyList<MBankPdfRow> TwoPage => MBankSampleData.TwoPageRows;

    private static ParseResult Parse(byte[] pdf, int maxPages = MBankPdfParser.DefaultMaxPages) =>
        new MBankPdfParser(maxPages).Parse(new MemoryStream(pdf));

    private static bool CanParse(byte[] bytes, int maxPages = MBankPdfParser.DefaultMaxPages) =>
        new MBankPdfParser(maxPages).CanParse(new MemoryStream(bytes));

    // What a faithful parse of the rows returns: booking date, signed amount, and the description
    // lines joined with single spaces.
    private static List<NormalizedTransaction> Expected(IEnumerable<MBankPdfRow> rows) =>
        rows.Select(r => new NormalizedTransaction(r.BookingDate, string.Join(" ", r.DescriptionLines), r.Amount)).ToList();

    private static byte[] BuildTwoPagePdf(IReadOnlyList<MBankPdfRow> rows, MBankPdfLayout? layout = null, MBankPdfHeader? header = null) =>
        MBankPdfBuilder.Build(header ?? MBankSampleData.Header, MBankSampleData.OpeningBalance, rows, layout ?? MBankSampleData.TwoPageLayout);

    private static List<MBankPdfRow> WithRow(int index, MBankPdfRow replacement)
    {
        var rows = TwoPage.ToList();
        rows[index] = replacement;
        return rows;
    }

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

    // The message must have exactly this shape: the failing check, the page, the row's booking
    // date when readable, and a closing sentence without digits — so no amount, description or name fits in.
    private static void AssertRejected(byte[] pdf, string check, int page, string? bookingDate)
    {
        var exception = Assert.Throws<StatementIntegrityException>(() => Parse(pdf));

        var match = Regex.Match(
            exception.Message,
            @"^mBank statement rejected: (?<check>[^()]+) \(page (?<page>\d+)(, transaction date (?<date>\d{2}\.\d{2}\.\d{4}))?\)\. [^0-9]*$");
        Assert.True(match.Success, exception.Message);
        Assert.Equal(check, match.Groups["check"].Value);
        Assert.Equal(page.ToString(), match.Groups["page"].Value);
        Assert.Equal(bookingDate ?? string.Empty, match.Groups["date"].Value);

        foreach (var personal in new[] { MBankSampleData.Header.HolderName, MBankSampleData.Header.AccountNumber, "TESTOW", "PRZYKŁAD", "PRÓBNY", "ZAKUP", "BLIK", "PRZELEW" })
        {
            Assert.DoesNotContain(personal, exception.Message);
        }
    }

    [Fact]
    public void Parser_IdentifiesItselfAsTheMBankPdfParser()
    {
        // Arrange
        var parser = new MBankPdfParser();

        // Assert
        Assert.Equal("mBank", parser.BankName);
        Assert.Equal(StatementFormat.Pdf, parser.Format);
    }

    [Theory]
    [InlineData(TwoPageFixture)]
    [InlineData(MultiPageFixture)]
    public void CanParse_ReturnsTrue_ForBothSyntheticFixtures(string fileName)
    {
        // Act & Assert
        Assert.True(CanParse(ReadFixture(fileName)));
    }

    [Theory]
    [InlineData("mbank-sample-redacted.csv")]
    [InlineData("mbank-sample-redacted-dmy.csv")]
    [InlineData("mbank-sample-utf8-remojibake.csv")]
    [InlineData("mbank-sample-comma-wrapped-utf8-remojibake.csv")]
    [InlineData("erste-sample-redacted.csv")]
    [InlineData("erste-sample-redacted-tab.csv")]
    [InlineData("erste-sample-redacted-pipe.csv")]
    [InlineData("erste-sample-redacted-comma.csv")]
    public void CanParse_ReturnsFalseWithoutThrowing_ForCsvFixtures(string fileName)
    {
        // Arrange
        using var stream = File.OpenRead(FixturePath(fileName));

        // Act & Assert
        Assert.False(new MBankPdfParser().CanParse(stream));
    }

    [Theory]
    [InlineData("velobank-sample-synthetic.pdf")]
    [InlineData("velobank-sample-synthetic-multipage.pdf")]
    public void CanParse_ReturnsFalse_ForVeloBankPdfFixtures(string fileName)
    {
        // Act & Assert
        Assert.False(CanParse(ReadFixture(fileName)));
    }

    [Theory]
    [InlineData(TwoPageFixture)]
    [InlineData(MultiPageFixture)]
    public void VeloBankParser_ReturnsFalse_ForMBankPdfFixtures(string fileName)
    {
        // Act & Assert
        Assert.False(new VeloBankPdfParser().CanParse(new MemoryStream(ReadFixture(fileName))));
    }

    [Fact]
    public void CanParse_ReturnsFalse_ForPdfThatIsNotAnMBankStatement()
    {
        // Arrange
        var pdf = BuildTextPdf("Historia rachunku", "VeloBank S.A.", "DATA TRANSAKCJI DATA KSIĘGOWANIA OPIS KWOTA SALDO");

        // Act & Assert
        Assert.False(CanParse(pdf));
    }

    [Theory]
    [InlineData("Elektroniczne zestawienie operacji", "mBank S.A.", "Data księgowania Opis operacji Kwota Saldo po operacji", true)]
    [InlineData("Elektroniczne zestawienie operacji", "Data księgowania Opis operacji Kwota Saldo po operacji", "", false)]
    [InlineData("Elektroniczne zestawienie operacji", "mBank S.A.", "", false)]
    [InlineData("mBank S.A.", "Data księgowania Opis operacji Kwota Saldo po operacji", "", false)]
    public void CanParse_RequiresTheTitleTheBankNameAndTheTableHeader(string line1, string line2, string line3, bool expected)
    {
        // Act & Assert
        Assert.Equal(expected, CanParse(BuildTextPdf(line1, line2, line3)));
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
        var parser = new MBankPdfParser();
        using var stream = new MemoryStream(ReadFixture(TwoPageFixture));

        // Act & Assert
        Assert.True(parser.CanParse(stream));

        // Assert
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void CanParse_ReadsFromTheCurrentPosition_AndRestoresIt()
    {
        // Arrange
        var parser = new MBankPdfParser();
        using var stream = new MemoryStream([.. new byte[7], .. ReadFixture(TwoPageFixture)]);
        stream.Position = 7;

        // Act & Assert
        Assert.True(parser.CanParse(stream));

        // Assert
        Assert.Equal(7, stream.Position);
    }

    [Fact]
    public void Parse_ReturnsTwentyRowsInOrder_ForTwoPageFixture()
    {
        // Act
        var result = Parse(ReadFixture(TwoPageFixture));

        // Assert
        Assert.Equal(MBankSampleData.TwoPageRowCount, result.Transactions.Count);
        Assert.Equal(0, result.SkippedErrorCount);
        Assert.Equal(Expected(TwoPage), result.Transactions);
    }

    [Fact]
    public void Parse_ReturnsRowsOldestFirst_WithTheBookingDate()
    {
        // Act
        var result = Parse(ReadFixture(TwoPageFixture));

        // Assert
        Assert.Equal(result.Transactions.OrderBy(t => t.Date).ToList(), result.Transactions);
        Assert.Equal(new DateOnly(2026, 9, 10), result.Transactions[0].Date);
        // Operated 2026-09-11, booked 2026-09-12.
        var delayed = result.Transactions[3];
        Assert.Equal(new DateOnly(2026, 9, 12), delayed.Date);
        Assert.Equal(-89.99m, delayed.Amount);
        Assert.Contains("DATA TRANSAKCJI: 2026-09-11", delayed.Description);
    }

    [Fact]
    public void Parse_ReadsSignedAmounts_IncludingOnesOfAThousandOrMore()
    {
        // Act
        var result = Parse(ReadFixture(TwoPageFixture));

        // Assert
        Assert.Contains(result.Transactions, t => t.Amount == 3200.00m && t.Description.StartsWith("PRZELEW ZEWNĘTRZNY PRZYCHODZĄCY"));
        Assert.Contains(result.Transactions, t => t.Amount == -1200.00m);
        Assert.Contains(result.Transactions, t => t.Amount == 25.00m && t.Description.StartsWith("POS ZWROT TOWARU"));
        Assert.Contains(result.Transactions, t => t.Amount == -142.35m);
    }

    [Fact]
    public void Parse_JoinsEveryLineOfAFiveLineDescriptionWithSingleSpaces()
    {
        // Arrange
        var fiveLines = TwoPage[IncomingTransferRow];
        Assert.Equal(5, fiveLines.DescriptionLines.Count);

        // Act
        var result = Parse(ReadFixture(TwoPageFixture));

        // Assert
        var description = result.Transactions[IncomingTransferRow].Description;
        Assert.Equal(
            "PRZELEW ZEWNĘTRZNY PRZYCHODZĄCY BIURO PRZYKŁADOWE SP. Z O.O. UL. TESTOWA 1 00-000 TESTOWO 11 1111 1111 1111 1111 1111 1111 WYNAGRODZENIE ZA WRZESIEŃ 2026",
            description);
        Assert.DoesNotContain("  ", description);
        Assert.Equal(description.Trim(), description);
    }

    [Fact]
    public void Parse_KeepsPolishDiacritics()
    {
        // Act
        var result = Parse(ReadFixture(TwoPageFixture));

        // Assert
        Assert.Contains(result.Transactions, t => t.Description.Contains("ZAKUP PRZY UŻYCIU KARTY"));
        Assert.Contains(result.Transactions, t => t.Description.Contains("BLIK P2P-WYCHODZĄCY"));
        Assert.Contains(result.Transactions, t => t.Description.Contains("APTEKA PRZYKŁADOWA"));
        Assert.Contains(result.Transactions, t => t.Description.Contains("ADAM PRÓBNY"));
        Assert.Contains(result.Transactions, t => t.Description.Contains("WYNAGRODZENIE ZA WRZESIEŃ 2026"));
    }

    [Fact]
    public void Parse_ReturnsFortyRowsInOrder_ForMultiPageFixture()
    {
        // Act
        var result = Parse(ReadFixture(MultiPageFixture));

        // Assert
        Assert.Equal(MBankSampleData.MultiPageRowCount, result.Transactions.Count);
        Assert.Equal(0, result.SkippedErrorCount);
        Assert.Equal(Expected(MBankSampleData.MultiPageRows), result.Transactions);
    }

    [Fact]
    public void Parse_TakesNoHeaderFooterOrBalanceTextIntoRows()
    {
        // The multi-page fixture repeats the table header and the page footer on each of its three
        // pages; the last page also carries the closing balance and the boilerplate paragraph.
        foreach (var fixture in new[] { TwoPageFixture, MultiPageFixture })
        {
            // Act
            var result = Parse(ReadFixture(fixture));

            // Assert
            Assert.All(result.Transactions, t =>
            {
                Assert.DoesNotContain("księgowania", t.Description);
                Assert.DoesNotContain("Strona", t.Description);
                Assert.DoesNotContain("Saldo", t.Description);
                Assert.DoesNotContain("Niniejsze zestawienie", t.Description);
                Assert.DoesNotContain("Koniec dokumentu", t.Description);
                Assert.DoesNotContain("Podsumowanie", t.Description);
            });
        }
    }

    [Fact]
    public void Parse_ReturnsTheSameKeysForTheSharedRows_OfTheShortAndTheLongExport()
    {
        // Act
        var shortExport = Parse(ReadFixture(TwoPageFixture));
        var longExport = Parse(ReadFixture(MultiPageFixture));

        // Assert
        Assert.Equal(shortExport.Transactions, longExport.Transactions.Take(shortExport.Transactions.Count));
    }

    [Theory]
    [InlineData(10, 10)]
    [InlineData(12, 14)]
    [InlineData(11, 18)]
    [InlineData(6, 20)]
    public void Parse_ReturnsTheSameRows_WhateverThePageBreaks(int firstPageRows, int rowsPerPage)
    {
        // Arrange
        var layout = new MBankPdfLayout { FirstPageRows = firstPageRows, RowsPerPage = rowsPerPage };
        var pdf = MBankPdfBuilder.Build(MBankSampleData.Header, MBankSampleData.OpeningBalance, MBankSampleData.MultiPageRows, layout);

        // Act
        var result = Parse(pdf);

        // Assert
        Assert.Equal(Expected(MBankSampleData.MultiPageRows), result.Transactions);
    }

    // The real single-page export (checked manually; it holds personal data and stays outside the
    // repository) draws every row as its own cell with a small gap, so the table has grid lines that
    // carry exactly five segments. The builder reproduces that with RowGap.
    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(8)]
    public void Parse_ReadsAStatementThatFitsOnOnePage(int rowCount)
    {
        // Arrange
        var rows = MBankSampleData.WithRunningBalances(MBankSampleData.OpeningBalance, MBankSampleData.TwoPageRows.Take(rowCount).ToList());
        var layout = new MBankPdfLayout { FirstPageRows = rowCount, RowGap = 0.5 };
        var pdf = MBankPdfBuilder.Build(MBankSampleData.Header, MBankSampleData.OpeningBalance, rows, layout);

        // Act
        var result = Parse(pdf);

        // Assert
        Assert.True(CanParse(pdf));
        Assert.Equal(Expected(rows), result.Transactions);
        Assert.Equal(0, result.SkippedErrorCount);
    }

    [Fact]
    public void Parse_ReadsAStatementWithoutAnyRow()
    {
        // Arrange
        var pdf = MBankPdfBuilder.Build(MBankSampleData.Header, MBankSampleData.OpeningBalance, [], MBankSampleData.TwoPageLayout);

        // Act
        var result = Parse(pdf);

        // Assert
        Assert.Empty(result.Transactions);
        Assert.Equal(0, result.SkippedErrorCount);
    }

    [Fact]
    public void Parse_AssemblesAmountsAndBalancesPrintedWithSeveralThousandsGroups()
    {
        // Arrange
        var rows = TwoPage.ToList();
        rows[PageOneRow] = rows[PageOneRow] with { Amount = -1250.40m };
        var rebalanced = MBankSampleData.WithRunningBalances(1_234_567.89m, rows);

        var result = MBankPdfBuilder.Build(MBankSampleData.Header, 1_234_567.89m, rebalanced, MBankSampleData.TwoPageLayout);

        // Act
        var parsed = Parse(result);

        // Assert
        Assert.Equal(Expected(rebalanced), parsed.Transactions);
        Assert.Contains(parsed.Transactions, t => t.Amount == -1250.40m);
    }

    [Fact]
    public void Parse_Rejects_ATamperedBalance()
    {
        // Arrange
        var tampered = TwoPage[PageOneRow] with { Balance = TwoPage[PageOneRow].Balance + 5m };

        // Act & Assert
        AssertRejected(BuildTwoPagePdf(WithRow(PageOneRow, tampered)), BalanceCheck, page: 1, bookingDate: "13.09.2026");
    }

    [Fact]
    public void Parse_Rejects_ATamperedBalanceOnALaterPage_NamingThatPage()
    {
        // Arrange
        var tampered = TwoPage[SecondPageRow] with { Balance = TwoPage[SecondPageRow].Balance - 1m };

        // Act & Assert
        // Row 15 (index 14) is on page 2 (page 1 holds 12 rows); booked 2026-09-18.
        AssertRejected(BuildTwoPagePdf(WithRow(SecondPageRow, tampered)), BalanceCheck, page: 2, bookingDate: "18.09.2026");
    }

    [Fact]
    public void Parse_Rejects_ATamperedClosingBalance()
    {
        // Arrange
        var layout = MBankSampleData.TwoPageLayout with { ClosingBalanceOverride = TwoPage[^1].Balance + 10m };

        // Act & Assert
        AssertRejected(BuildTwoPagePdf(TwoPage, layout), ClosingBalanceCheck, page: 2, bookingDate: null);
    }

    [Fact]
    public void Parse_Rejects_ATamperedSummaryCount()
    {
        // Arrange
        var real = MBankPdfBuilder.ComputeSummary(TwoPage);
        var layout = MBankSampleData.TwoPageLayout with { SummaryOverride = real with { CreditCount = real.CreditCount + 1 } };

        // Act & Assert
        AssertRejected(BuildTwoPagePdf(TwoPage, layout), SummaryCheck, page: 1, bookingDate: null);
    }

    [Fact]
    public void Parse_Rejects_ATamperedSummaryCountOfDebits()
    {
        // Arrange
        var real = MBankPdfBuilder.ComputeSummary(TwoPage);
        var layout = MBankSampleData.TwoPageLayout with { SummaryOverride = real with { DebitCount = real.DebitCount - 1 } };

        // Act & Assert
        AssertRejected(BuildTwoPagePdf(TwoPage, layout), SummaryCheck, page: 1, bookingDate: null);
    }

    [Fact]
    public void Parse_Rejects_ATamperedSummarySum()
    {
        // Arrange
        var real = MBankPdfBuilder.ComputeSummary(TwoPage);
        var layout = MBankSampleData.TwoPageLayout with { SummaryOverride = real with { DebitSum = real.DebitSum + 0.01m } };

        // Act & Assert
        AssertRejected(BuildTwoPagePdf(TwoPage, layout), SummaryCheck, page: 1, bookingDate: null);
    }

    [Fact]
    public void Parse_Rejects_AMissingOpeningBalance()
    {
        // Arrange
        var layout = MBankSampleData.TwoPageLayout with { OmitOpeningBalance = true };

        // Act & Assert
        AssertRejected(BuildTwoPagePdf(TwoPage, layout), MissingOpeningCheck, page: 1, bookingDate: null);
    }

    [Fact]
    public void Parse_Rejects_AMissingClosingBalance_NamingTheLastPage()
    {
        // Arrange
        var layout = MBankSampleData.TwoPageLayout with { OmitClosingBalance = true };

        // Act & Assert
        AssertRejected(BuildTwoPagePdf(TwoPage, layout), MissingClosingCheck, page: 2, bookingDate: null);
    }

    [Fact]
    public void Parse_Rejects_AMissingSummary()
    {
        // Arrange
        var layout = MBankSampleData.TwoPageLayout with { OmitSummary = true };

        // Act & Assert
        AssertRejected(BuildTwoPagePdf(TwoPage, layout), MissingSummaryCheck, page: 1, bookingDate: null);
    }

    [Fact]
    public void Parse_Rejects_AnUnreadableAmountCell_WithTheRowsDate()
    {
        // Arrange
        var unreadable = TwoPage[PageOneRow] with { AmountText = "n/a" };

        // Act & Assert
        AssertRejected(BuildTwoPagePdf(WithRow(PageOneRow, unreadable)), UnreadableRowCheck, page: 1, bookingDate: "13.09.2026");
    }

    // Far beyond decimal range: must be an unreadable cell (422), not an OverflowException (500).
    [Theory]
    [InlineData("99999999999999999999999999999,99")]
    [InlineData("12345678901234567,89")]
    public void Parse_Rejects_AnAbsurdlyLargeAmountCell_AsUnreadable(string amountText)
    {
        // Arrange
        var unreadable = TwoPage[PageOneRow] with { AmountText = amountText };

        // Act & Assert
        AssertRejected(BuildTwoPagePdf(WithRow(PageOneRow, unreadable)), UnreadableRowCheck, page: 1, bookingDate: "13.09.2026");
    }

    [Fact]
    public void Parse_Rejects_AnUnreadableBalanceCell_WithTheRowsDate()
    {
        // Arrange
        var unreadable = TwoPage[SecondPageRow] with { BalanceText = "brak" };

        // Act & Assert
        AssertRejected(BuildTwoPagePdf(WithRow(SecondPageRow, unreadable)), UnreadableRowCheck, page: 2, bookingDate: "18.09.2026");
    }

    [Fact]
    public void Parse_Rejects_AnUnreadableBookingDate_WithoutADate()
    {
        // Arrange
        var unreadable = TwoPage[PageOneRow] with { BookingDateText = "??-??-????" };

        // Act & Assert
        AssertRejected(BuildTwoPagePdf(WithRow(PageOneRow, unreadable)), UnreadableRowCheck, page: 1, bookingDate: null);
    }

    [Fact]
    public void Parse_ReturnsNoTransactionsAndSkipsEveryRow_WhenTheStatementIsNotInPln()
    {
        // Arrange
        var header = MBankSampleData.Header with { Currency = "EUR" };

        // Act
        var result = Parse(BuildTwoPagePdf(TwoPage, header: header));

        // Assert
        Assert.Empty(result.Transactions);
        Assert.Equal(MBankSampleData.TwoPageRowCount, result.SkippedErrorCount);
    }

    [Fact]
    public void Parse_StillChecksTheFigures_OfAStatementThatIsNotInPln()
    {
        // Arrange
        var header = MBankSampleData.Header with { Currency = "EUR" };
        var tampered = TwoPage[PageOneRow] with { Balance = TwoPage[PageOneRow].Balance + 5m };

        // Act & Assert
        Assert.Throws<StatementIntegrityException>(() => Parse(BuildTwoPagePdf(WithRow(PageOneRow, tampered), header: header)));
    }

    [Fact]
    public void Parse_ReturnsNothing_ForPdfThatIsNotAnMBankStatement()
    {
        // Act
        var result = Parse(BuildTextPdf("Historia rachunku", "VeloBank S.A."));

        // Assert
        Assert.Empty(result.Transactions);
        Assert.Equal(0, result.SkippedErrorCount);
    }

    [Fact]
    public void Parse_ReturnsNothing_ForAnMBankPageWithoutATable()
    {
        // Act
        var result = Parse(BuildTextPdf("Elektroniczne zestawienie operacji", "mBank S.A.", "Data księgowania Opis operacji Kwota Saldo po operacji"));

        // Assert
        Assert.Empty(result.Transactions);
        Assert.Equal(0, result.SkippedErrorCount);
    }

    [Fact]
    public void Parse_ReturnsNothing_ForVeloBankPdfFixtures()
    {
        foreach (var fixture in new[] { "velobank-sample-synthetic.pdf", "velobank-sample-synthetic-multipage.pdf" })
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
        foreach (var bytes in new[] { PdfMagicFollowedByGarbage(), Array.Empty<byte>(), "not a pdf at all"u8.ToArray(), File.ReadAllBytes(FixturePath("mbank-sample-redacted.csv")) })
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
        var parser = new MBankPdfParser();
        using var stream = new MemoryStream([.. new byte[7], .. ReadFixture(TwoPageFixture)]);
        stream.Position = 7;

        // Act
        var result = parser.Parse(stream);

        // Assert
        Assert.Equal(MBankSampleData.TwoPageRowCount, result.Transactions.Count);
        Assert.Equal(7, stream.Position);
    }

    [Fact]
    public void CanParseThenParse_WorksOnOneStream()
    {
        // Arrange
        var parser = new MBankPdfParser();
        using var stream = new MemoryStream(ReadFixture(MultiPageFixture));

        // Act & Assert
        Assert.True(parser.CanParse(stream));
        var result = parser.Parse(stream);

        // Assert
        Assert.Equal(MBankSampleData.MultiPageRowCount, result.Transactions.Count);
    }
}
