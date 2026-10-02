using System.Text;
using System.Text.RegularExpressions;
using MyFinances.Api.Import;
using MyFinances.Api.Tests.Support;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace MyFinances.Api.Tests;

// Runs the parser over the synthetic VeloBank PDFs (committed fixtures and variants generated in
// memory by VeloBankPdfBuilder). The real statements contain personal data and are only used for a
// manual check outside the repository.
public class VeloBankPdfParserTests
{
    private const string OnePageFixture = "velobank-sample-synthetic.pdf";
    private const string MultiPageFixture = "velobank-sample-synthetic-multipage.pdf";

    // The three integrity checks, as the parser names them in its messages.
    private const string BalanceCheck = "the running balance does not add up";
    private const string CardAmountCheck = "the card amount in the description differs from the amount column";
    private const string UnreadableRowCheck = "a table row could not be read";

    // Indexes into VeloBankSampleData.OnePageRows (newest first; 0 and 1 are pending).
    private const int FirstBookedRow = 2;
    private const int BalanceTamperRow = 10;      // 24.09.2026, a transfer
    private const int CardTamperRow = 12;         // 22.09.2026, a card payment
    private const int ForeignCurrencyRow = 8;     // 26.09.2026, a card payment
    private const int FiveLineDescriptionRow = 11;

    private static string FixturePath(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private static byte[] ReadFixture(string name) => File.ReadAllBytes(FixturePath(name));

    private static IReadOnlyList<VeloBankPdfRow> OnePage => VeloBankSampleData.OnePageRows;

    private static ParseResult Parse(byte[] pdf, int maxPages = VeloBankPdfParser.DefaultMaxPages) =>
        new VeloBankPdfParser(maxPages).Parse(new MemoryStream(pdf));

    private static bool CanParse(byte[] bytes, int maxPages = VeloBankPdfParser.DefaultMaxPages) =>
        new VeloBankPdfParser(maxPages).CanParse(new MemoryStream(bytes));

    // What a faithful parse of the rows returns: transaction date, signed amount, and the
    // description lines joined with single spaces.
    private static List<NormalizedTransaction> Expected(IEnumerable<VeloBankPdfRow> rows) =>
        rows.Select(r => new NormalizedTransaction(r.TransactionDate, string.Join(" ", r.DescriptionLines), r.Amount)).ToList();

    private static byte[] BuildOnePagePdf(IReadOnlyList<VeloBankPdfRow> rows) =>
        VeloBankPdfBuilder.Build(VeloBankSampleData.Header, rows, VeloBankSampleData.OnePageLayout);

    private static List<VeloBankPdfRow> WithRow(int index, VeloBankPdfRow replacement)
    {
        var rows = OnePage.ToList();
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

    // The message must have exactly this shape: the failing check, the page, the row's transaction
    // date when readable, and a closing sentence without digits — so no amount, description or name fits in.
    private static void AssertRejected(byte[] pdf, string check, int page, string? transactionDate)
    {
        var exception = Assert.Throws<StatementIntegrityException>(() => Parse(pdf));

        var match = Regex.Match(
            exception.Message,
            @"^VeloBank statement rejected: (?<check>[^()]+) \(page (?<page>\d+)(, transaction date (?<date>\d{2}\.\d{2}\.\d{4}))?\)\. [^0-9]*$");
        Assert.True(match.Success, exception.Message);
        Assert.Equal(check, match.Groups["check"].Value);
        Assert.Equal(page.ToString(), match.Groups["page"].Value);
        Assert.Equal(transactionDate ?? string.Empty, match.Groups["date"].Value);

        foreach (var personal in new[] { VeloBankSampleData.Header.HolderName, VeloBankSampleData.Header.AddressLine, "TESTOW", "PRZYKŁAD", "PRÓBNY", "Operacja", "Przelew" })
        {
            Assert.DoesNotContain(personal, exception.Message);
        }
    }

    [Fact]
    public void Parser_IdentifiesItselfAsTheVeloBankPdfParser()
    {
        var parser = new VeloBankPdfParser();

        Assert.Equal("VeloBank", parser.BankName);
        Assert.Equal(StatementFormat.Pdf, parser.Format);
    }

    [Theory]
    [InlineData(OnePageFixture)]
    [InlineData(MultiPageFixture)]
    public void CanParse_ReturnsTrue_ForBothSyntheticFixtures(string fileName)
    {
        Assert.True(CanParse(ReadFixture(fileName)));
    }

    [Theory]
    [InlineData("mbank-sample-redacted.csv")]
    [InlineData("mbank-sample-redacted-dmy.csv")]
    [InlineData("mbank-sample-utf8-remojibake.csv")]
    [InlineData("erste-sample-redacted.csv")]
    [InlineData("erste-sample-redacted-tab.csv")]
    [InlineData("erste-sample-redacted-pipe.csv")]
    [InlineData("erste-sample-redacted-comma.csv")]
    public void CanParse_ReturnsFalseWithoutThrowing_ForCsvFixtures(string fileName)
    {
        using var stream = File.OpenRead(FixturePath(fileName));

        Assert.False(new VeloBankPdfParser().CanParse(stream));
    }

    [Fact]
    public void CanParse_ReturnsFalse_ForPdfThatIsNotAVeloBankStatement()
    {
        var pdf = BuildTextPdf("Elektroniczne zestawienie operacji", "mBank S.A.", "Saldo początkowe");

        Assert.False(CanParse(pdf));
    }

    [Theory]
    [InlineData("Historia rachunku", "VeloBank S.A.", "DATA TRANSAKCJI DATA KSIĘGOWANIA OPIS KWOTA SALDO", true)]
    [InlineData("Historia rachunku", "DATA TRANSAKCJI DATA KSIĘGOWANIA OPIS KWOTA SALDO", "", false)]
    [InlineData("Historia rachunku", "VeloBank S.A.", "", false)]
    [InlineData("VeloBank S.A.", "DATA TRANSAKCJI DATA KSIĘGOWANIA OPIS KWOTA SALDO", "", false)]
    public void CanParse_RequiresTheTitleTheBankNameAndTheTableHeader(string line1, string line2, string line3, bool expected)
    {
        Assert.Equal(expected, CanParse(BuildTextPdf(line1, line2, line3)));
    }

    [Fact]
    public void CanParse_ReturnsFalse_ForPdfMagicFollowedByGarbage()
    {
        Assert.False(CanParse(PdfMagicFollowedByGarbage()));
    }

    [Fact]
    public void CanParse_ReturnsFalse_ForEmptyStream()
    {
        Assert.False(CanParse([]));
    }

    [Fact]
    public void CanParse_ReturnsFalse_WhenThePdfHasMoreThanMaxPages()
    {
        var threePages = ReadFixture(MultiPageFixture);

        Assert.False(CanParse(threePages, maxPages: 2));
        Assert.True(CanParse(threePages, maxPages: 3));
    }

    [Fact]
    public void CanParse_RestoresStreamPosition()
    {
        var parser = new VeloBankPdfParser();
        using var stream = new MemoryStream(ReadFixture(OnePageFixture));

        Assert.True(parser.CanParse(stream));

        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void CanParse_ReadsFromTheCurrentPosition_AndRestoresIt()
    {
        var parser = new VeloBankPdfParser();
        using var stream = new MemoryStream([.. new byte[7], .. ReadFixture(OnePageFixture)]);
        stream.Position = 7;

        Assert.True(parser.CanParse(stream));

        Assert.Equal(7, stream.Position);
    }

    [Fact]
    public void Parse_ReturnsSeventeenRowsInOrder_ForOnePageFixture()
    {
        var result = Parse(ReadFixture(OnePageFixture));

        Assert.Equal(VeloBankSampleData.OnePageRowCount, result.Transactions.Count);
        Assert.Equal(0, result.SkippedErrorCount);
        Assert.Equal(Expected(OnePage), result.Transactions);
    }

    [Fact]
    public void Parse_ReturnsPendingRowsLikeAnyOtherRow()
    {
        var result = Parse(ReadFixture(OnePageFixture));

        var pending = OnePage.Where(r => r.BookingDate is null).ToList();
        Assert.Equal(2, pending.Count);
        Assert.Equal(Expected(pending), result.Transactions.Take(pending.Count));
    }

    [Fact]
    public void Parse_UsesTheTransactionDateRatherThanTheBookingDate()
    {
        var result = Parse(ReadFixture(OnePageFixture));

        // Paid 29.09.2026, booked 30.09.2026.
        var row = result.Transactions[FirstBookedRow];
        Assert.Equal(new DateOnly(2026, 9, 29), row.Date);
        Assert.Equal(-8.99m, row.Amount);
    }

    [Fact]
    public void Parse_ReadsSignedAmounts()
    {
        var result = Parse(ReadFixture(OnePageFixture));

        Assert.Contains(result.Transactions, t => t.Amount == 300.00m && t.Description.Contains("Zwrot za bilety"));
        Assert.Contains(result.Transactions, t => t.Amount == 1200.00m);
        Assert.Contains(result.Transactions, t => t.Amount == -420.00m);
    }

    [Fact]
    public void Parse_JoinsEveryLineOfAFiveLineDescriptionWithSingleSpaces()
    {
        var fiveLines = OnePage[FiveLineDescriptionRow];
        Assert.Equal(5, fiveLines.DescriptionLines.Count);

        var result = Parse(ReadFixture(OnePageFixture));

        var description = result.Transactions[FiveLineDescriptionRow].Description;
        Assert.Contains("Wynagrodzenie za wrzesień 2026 - rozliczenie godzin nadliczbowych oraz premii kwartalnej zgodnie z aneksem nr 1", description);
        Assert.DoesNotContain("  ", description);
        Assert.Equal(description.Trim(), description);
    }

    [Fact]
    public void Parse_KeepsPolishDiacritics()
    {
        var result = Parse(ReadFixture(OnePageFixture));

        Assert.Contains(result.Transactions, t => t.Description.Contains("Tytuł: Czynsz za wrzesień 2026") && t.Description.Contains("ADAM PRÓBNY"));
        Assert.Contains(result.Transactions, t => t.Description.Contains("FIRMA PRZYKŁADOWA SP. Z O.O."));
        Assert.Contains(result.Transactions, t => t.Description.Contains("Operacja kartą"));
    }

    [Fact]
    public void Parse_ReturnsFortyRowsInOrder_ForMultiPageFixture()
    {
        var result = Parse(ReadFixture(MultiPageFixture));

        Assert.Equal(VeloBankSampleData.MultiPageRowCount, result.Transactions.Count);
        Assert.Equal(0, result.SkippedErrorCount);
        Assert.Equal(Expected(VeloBankSampleData.MultiPageRows), result.Transactions);
    }

    [Fact]
    public void Parse_TakesNoHeaderOrFooterTextIntoRows()
    {
        // The multi-page fixture repeats the header and the footer on each of its three pages;
        // the one-page fixture has a second header block in the middle of its page.
        foreach (var fixture in new[] { OnePageFixture, MultiPageFixture })
        {
            var result = Parse(ReadFixture(fixture));

            Assert.All(result.Transactions, t =>
            {
                Assert.DoesNotContain("TRANSAKCJI", t.Description);
                Assert.DoesNotContain("KSIĘGOWANIA", t.Description);
                Assert.DoesNotContain("VeloBank", t.Description);
                Assert.DoesNotContain("Koniec dokumentu", t.Description);
            });
        }
    }

    [Fact]
    public void Parse_ReturnsTheSameKeysForTheSharedRows_OfTheShortAndTheLongExport()
    {
        var shortExport = Parse(ReadFixture(OnePageFixture));
        var longExport = Parse(ReadFixture(MultiPageFixture));

        Assert.Equal(shortExport.Transactions, longExport.Transactions.Take(shortExport.Transactions.Count));
    }

    [Theory]
    [InlineData(10, 10, null)]
    [InlineData(15, 19, 5)]
    [InlineData(12, 7, 11)]
    [InlineData(17, 19, 15)]
    public void Parse_ReturnsTheSameKeys_WhateverThePageBreaks(int firstPageRows, int rowsPerPage, int? midPageHeaderAfterRows)
    {
        var layout = new VeloBankPdfLayout
        {
            FirstPageRows = firstPageRows,
            RowsPerPage = rowsPerPage,
            MidPageHeaderAfterRows = midPageHeaderAfterRows,
        };
        var pdf = VeloBankPdfBuilder.Build(VeloBankSampleData.Header, VeloBankSampleData.MultiPageRows, layout);

        var result = Parse(pdf);

        Assert.Equal(Expected(VeloBankSampleData.MultiPageRows), result.Transactions);
    }

    [Fact]
    public void Parse_ReadsValuesCenteredInTheRow_AsWellAsTopAligned()
    {
        var centred = new VeloBankPdfLayout { FirstPageRows = VeloBankSampleData.OnePageRowCount, MidPageHeaderAfterRows = 15, CenterValueCells = true };

        var onePage = Parse(VeloBankPdfBuilder.Build(VeloBankSampleData.Header, OnePage, centred));
        var multiPage = Parse(VeloBankPdfBuilder.Build(VeloBankSampleData.Header, VeloBankSampleData.MultiPageRows, new VeloBankPdfLayout { CenterValueCells = true }));

        Assert.Equal(Parse(ReadFixture(OnePageFixture)).Transactions, onePage.Transactions);
        Assert.Equal(Expected(VeloBankSampleData.MultiPageRows), multiPage.Transactions);
    }

    [Fact]
    public void Parse_AssemblesAmountsAndBalancesPrintedWithSeveralThousandsGroups()
    {
        var rows = OnePage.ToList();
        rows[CardTamperRow] = rows[CardTamperRow] with
        {
            Amount = -1250.40m,
            DescriptionLines = VeloBankSampleData.CardLines(-1250.40m, "PLN", "SKLEP TESTOWY 12"),
        };
        var rebalanced = VeloBankSampleData.WithRunningBalances(rows, 1_234_567.89m);
        Assert.Equal(1_234_567.89m, rebalanced[FirstBookedRow].Balance);

        var result = Parse(BuildOnePagePdf(rebalanced));

        Assert.Equal(Expected(rebalanced), result.Transactions);
        Assert.Contains(result.Transactions, t => t.Amount == -1250.40m);
    }

    [Fact]
    public void Parse_IgnoresPendingRowsInTheBalanceChain_WhereverTheyAre()
    {
        // A pending row in the middle of the booked ones (its balance cell is never read); the
        // oldest row makes room so the page still fits.
        var rows = OnePage.Take(OnePage.Count - 1).ToList();
        rows.Insert(8, OnePage[0] with { BalanceText = "brak" });
        var layout = new VeloBankPdfLayout { FirstPageRows = rows.Count, MidPageHeaderAfterRows = 15 };

        var result = Parse(VeloBankPdfBuilder.Build(VeloBankSampleData.Header, rows, layout));

        Assert.Equal(Expected(rows), result.Transactions);
    }

    [Fact]
    public void Parse_ParsesAStatementThatHasOnlyPendingRows()
    {
        var pending = OnePage.Take(2).ToList();

        var result = Parse(VeloBankPdfBuilder.Build(VeloBankSampleData.Header, pending));

        Assert.Equal(Expected(pending), result.Transactions);
        Assert.Equal(0, result.SkippedErrorCount);
    }

    [Fact]
    public void Parse_SkipsForeignCurrencyRow_AndComparesNoBalancesAcrossIt()
    {
        var euro = OnePage[ForeignCurrencyRow] with
        {
            Amount = -20m,
            Currency = "EUR",
            DescriptionLines = VeloBankSampleData.CardLines(-20m, "EUR", "SKLEP ZAGRANICZNY"),
            Balance = 500m,
        };
        var rows = VeloBankSampleData.WithRunningBalances(WithRow(ForeignCurrencyRow, euro), 1024.50m).ToList();
        // The bank took the euro payment out of the PLN balance in a way the statement does not show:
        // from this row down the printed balances jump by 15 PLN.
        for (var i = ForeignCurrencyRow + 1; i < rows.Count; i++)
        {
            rows[i] = rows[i] with { Balance = rows[i].Balance + 15m };
        }

        var result = Parse(BuildOnePagePdf(rows));

        Assert.Equal(1, result.SkippedErrorCount);
        Assert.Equal(Expected(rows.Where((_, i) => i != ForeignCurrencyRow)), result.Transactions);
        Assert.DoesNotContain(result.Transactions, t => t.Description.Contains("ZAGRANICZNY"));
    }

    [Fact]
    public void Parse_RejectsTheSameBalanceJump_WhenNoForeignCurrencyRowSitsInBetween()
    {
        var rows = OnePage.ToList();
        for (var i = ForeignCurrencyRow + 1; i < rows.Count; i++)
        {
            rows[i] = rows[i] with { Balance = rows[i].Balance + 15m };
        }

        // The first row below the jump (25.09.2026) is the one whose balance does not follow.
        AssertRejected(BuildOnePagePdf(rows), BalanceCheck, page: 1, transactionDate: "25.09.2026");
    }

    [Fact]
    public void Parse_ImportsPlnCardRowWhoseDescriptionNamesAnotherCurrency_WithoutTheCardAmountCheck()
    {
        var abroad = OnePage[ForeignCurrencyRow] with
        {
            Amount = -86.40m,
            DescriptionLines = VeloBankSampleData.CardLines(-20m, "EUR", "SKLEP ZAGRANICZNY"),
        };
        var rows = VeloBankSampleData.WithRunningBalances(WithRow(ForeignCurrencyRow, abroad), 1024.50m);

        var result = Parse(BuildOnePagePdf(rows));

        Assert.Equal(0, result.SkippedErrorCount);
        Assert.Equal(Expected(rows), result.Transactions);
        Assert.Contains(result.Transactions, t => t.Amount == -86.40m && t.Description.Contains("na kwotę 20,00 EUR"));
    }

    [Fact]
    public void Parse_Rejects_ATamperedBalance()
    {
        var tampered = OnePage[BalanceTamperRow] with { Balance = OnePage[BalanceTamperRow].Balance + 5m };

        AssertRejected(BuildOnePagePdf(WithRow(BalanceTamperRow, tampered)), BalanceCheck, page: 1, transactionDate: "24.09.2026");
    }

    [Fact]
    public void Parse_Rejects_ATamperedBalanceOnALaterPage_NamingThatPage()
    {
        var rows = VeloBankSampleData.MultiPageRows.ToList();
        var tampered = rows[20] with { Balance = rows[20].Balance - 1m };
        rows[20] = tampered;

        // Row 21 (index 20) is on page 2 (page 1 holds 15 rows, page 2 the next 19); paid 14.09.2026.
        AssertRejected(VeloBankPdfBuilder.Build(VeloBankSampleData.Header, rows), BalanceCheck, page: 2, transactionDate: "14.09.2026");
    }

    [Fact]
    public void Parse_Rejects_ATamperedCardAmountInTheDescription()
    {
        var tampered = OnePage[CardTamperRow] with
        {
            DescriptionLines = VeloBankSampleData.CardLines(-35.81m, "PLN", "SKLEP TESTOWY 12"),
        };

        AssertRejected(BuildOnePagePdf(WithRow(CardTamperRow, tampered)), CardAmountCheck, page: 1, transactionDate: "22.09.2026");
    }

    [Fact]
    public void Parse_Rejects_AnUnreadableAmountCell_WithTheRowsDate()
    {
        var unreadable = OnePage[BalanceTamperRow] with { AmountText = "n/a" };

        AssertRejected(BuildOnePagePdf(WithRow(BalanceTamperRow, unreadable)), UnreadableRowCheck, page: 1, transactionDate: "24.09.2026");
    }

    // Far beyond decimal range: must be an unreadable cell (422), not an OverflowException (500).
    [Theory]
    [InlineData("99999999999999999999999999999,99 PLN")]
    [InlineData("12345678901234567,89 PLN")]
    public void Parse_Rejects_AnAbsurdlyLargeAmountCell_AsUnreadable(string amountText)
    {
        var unreadable = OnePage[BalanceTamperRow] with { AmountText = amountText };

        AssertRejected(BuildOnePagePdf(WithRow(BalanceTamperRow, unreadable)), UnreadableRowCheck, page: 1, transactionDate: "24.09.2026");
    }

    // A later page whose table cannot be found would silently lose its rows.
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void Parse_Rejects_ALaterPageWithoutATableHeader_NamingThatPage(int page)
    {
        var layout = VeloBankSampleData.MultiPageLayout with { OmitHeaderOnPage = page };
        var pdf = VeloBankPdfBuilder.Build(VeloBankSampleData.Header, VeloBankSampleData.MultiPageRows, layout);

        AssertRejected(pdf, UnreadableRowCheck, page, transactionDate: null);
    }

    [Fact]
    public void Parse_Rejects_AnUnreadableTransactionDate_WithoutADate()
    {
        var unreadable = OnePage[BalanceTamperRow] with { TransactionDateText = "??.??.????" };

        AssertRejected(BuildOnePagePdf(WithRow(BalanceTamperRow, unreadable)), UnreadableRowCheck, page: 1, transactionDate: null);
    }

    [Fact]
    public void Parse_Rejects_AnUnreadableBalanceOnABookedPlnRow()
    {
        var unreadable = OnePage[BalanceTamperRow] with { BalanceText = "brak" };

        AssertRejected(BuildOnePagePdf(WithRow(BalanceTamperRow, unreadable)), UnreadableRowCheck, page: 1, transactionDate: "24.09.2026");
    }

    [Fact]
    public void Parse_ReturnsNothing_ForPdfThatIsNotAVeloBankStatement()
    {
        var result = Parse(BuildTextPdf("Elektroniczne zestawienie operacji", "mBank S.A."));

        Assert.Empty(result.Transactions);
        Assert.Equal(0, result.SkippedErrorCount);
    }

    [Fact]
    public void Parse_ReturnsNothing_ForAVeloBankPageWithoutATable()
    {
        var result = Parse(BuildTextPdf("Historia rachunku", "VeloBank S.A.", "DATA TRANSAKCJI DATA KSIĘGOWANIA OPIS KWOTA SALDO"));

        Assert.Empty(result.Transactions);
        Assert.Equal(0, result.SkippedErrorCount);
    }

    [Fact]
    public void Parse_ReturnsNothingWithoutThrowing_ForCorruptContent()
    {
        foreach (var bytes in new[] { PdfMagicFollowedByGarbage(), Array.Empty<byte>(), "not a pdf at all"u8.ToArray(), File.ReadAllBytes(FixturePath("mbank-sample-redacted.csv")) })
        {
            var result = Parse(bytes);

            Assert.Empty(result.Transactions);
            Assert.Equal(0, result.SkippedErrorCount);
        }
    }

    [Fact]
    public void Parse_ReturnsNothing_WhenThePdfHasMoreThanMaxPages()
    {
        var result = Parse(ReadFixture(MultiPageFixture), maxPages: 2);

        Assert.Empty(result.Transactions);
        Assert.Equal(0, result.SkippedErrorCount);
    }

    [Fact]
    public void Parse_RestoresStreamPosition()
    {
        var parser = new VeloBankPdfParser();
        using var stream = new MemoryStream([.. new byte[7], .. ReadFixture(OnePageFixture)]);
        stream.Position = 7;

        var result = parser.Parse(stream);

        Assert.Equal(VeloBankSampleData.OnePageRowCount, result.Transactions.Count);
        Assert.Equal(7, stream.Position);
    }

    [Fact]
    public void CanParseThenParse_WorksOnOneStream()
    {
        var parser = new VeloBankPdfParser();
        using var stream = new MemoryStream(ReadFixture(MultiPageFixture));

        Assert.True(parser.CanParse(stream));
        var result = parser.Parse(stream);

        Assert.Equal(VeloBankSampleData.MultiPageRowCount, result.Transactions.Count);
    }
}
