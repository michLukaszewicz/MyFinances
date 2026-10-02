using System.Text;
using System.Text.RegularExpressions;
using MyFinances.Api.Import;
using MyFinances.Api.Tests.Support;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace MyFinances.Api.Tests.Import.Parsers;

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
        // Arrange
        var parser = new VeloBankPdfParser();

        // Assert
        Assert.Equal("VeloBank", parser.BankName);
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
    [InlineData("mbank-sample-redacted.csv")]
    [InlineData("mbank-sample-redacted-dmy.csv")]
    [InlineData("mbank-sample-utf8-remojibake.csv")]
    [InlineData("erste-sample-redacted.csv")]
    [InlineData("erste-sample-redacted-tab.csv")]
    [InlineData("erste-sample-redacted-pipe.csv")]
    [InlineData("erste-sample-redacted-comma.csv")]
    public void CanParse_ReturnsFalseWithoutThrowing_ForCsvFixtures(string fileName)
    {
        // Arrange
        using var stream = File.OpenRead(FixturePath(fileName));

        // Act & Assert
        Assert.False(new VeloBankPdfParser().CanParse(stream));
    }

    [Fact]
    public void CanParse_ReturnsFalse_ForPdfThatIsNotAVeloBankStatement()
    {
        // Arrange
        var pdf = BuildTextPdf("Elektroniczne zestawienie operacji", "mBank S.A.", "Saldo początkowe");

        // Act & Assert
        Assert.False(CanParse(pdf));
    }

    [Theory]
    [InlineData("Historia rachunku", "VeloBank S.A.", "DATA TRANSAKCJI DATA KSIĘGOWANIA OPIS KWOTA SALDO", true)]
    [InlineData("Historia rachunku", "DATA TRANSAKCJI DATA KSIĘGOWANIA OPIS KWOTA SALDO", "", false)]
    [InlineData("Historia rachunku", "VeloBank S.A.", "", false)]
    [InlineData("VeloBank S.A.", "DATA TRANSAKCJI DATA KSIĘGOWANIA OPIS KWOTA SALDO", "", false)]
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
        var parser = new VeloBankPdfParser();
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
        var parser = new VeloBankPdfParser();
        using var stream = new MemoryStream([.. new byte[7], .. ReadFixture(OnePageFixture)]);
        stream.Position = 7;

        // Act & Assert
        Assert.True(parser.CanParse(stream));

        // Assert
        Assert.Equal(7, stream.Position);
    }

    [Fact]
    public void Parse_ReturnsSeventeenRowsInOrder_ForOnePageFixture()
    {
        // Act
        var result = Parse(ReadFixture(OnePageFixture));

        // Assert
        Assert.Equal(VeloBankSampleData.OnePageRowCount, result.Transactions.Count);
        Assert.Equal(0, result.SkippedErrorCount);
        Assert.Equal(Expected(OnePage), result.Transactions);
    }

    // Transaction dates and amounts as printed in the table of the one-page fixture (the
    // "DATA TRANSAKCJI" and "KWOTA TRANSAKCJI" columns), written out by hand rather than derived
    // from the builder's formatting.
    [Fact]
    public void Parse_ReadsEveryTransactionDateAndAmount_AsPrintedInTheFixture()
    {
        // Arrange
        (DateOnly Date, decimal Amount)[] printed =
        [
            (new DateOnly(2026, 9, 30), -23.40m),
            (new DateOnly(2026, 9, 30), -112.05m),
            (new DateOnly(2026, 9, 29), -8.99m),
            (new DateOnly(2026, 9, 29), -150.00m),
            (new DateOnly(2026, 9, 28), -64.30m),
            (new DateOnly(2026, 9, 28), -19.99m),
            (new DateOnly(2026, 9, 27), 300.00m),
            (new DateOnly(2026, 9, 26), -42.15m),
            (new DateOnly(2026, 9, 26), -7.50m),
            (new DateOnly(2026, 9, 25), -89.00m),
            (new DateOnly(2026, 9, 24), -420.00m),
            (new DateOnly(2026, 9, 23), 1200.00m),
            (new DateOnly(2026, 9, 22), -35.80m),
            (new DateOnly(2026, 9, 21), -12.00m),
            (new DateOnly(2026, 9, 20), -75.00m),
            (new DateOnly(2026, 9, 19), -5.40m),
            (new DateOnly(2026, 9, 18), -27.60m),
        ];

        // Act
        var result = Parse(ReadFixture(OnePageFixture));

        // Assert
        Assert.Equal(printed, result.Transactions.Select(t => (t.Date, t.Amount)).ToArray());
    }

    [Fact]
    public void Parse_ReturnsPendingRowsLikeAnyOtherRow()
    {
        // Act
        var result = Parse(ReadFixture(OnePageFixture));

        // Assert
        var pending = OnePage.Where(r => r.BookingDate is null).ToList();
        Assert.Equal(2, pending.Count);
        Assert.Equal(Expected(pending), result.Transactions.Take(pending.Count));
    }

    [Fact]
    public void Parse_UsesTheTransactionDateRatherThanTheBookingDate()
    {
        // Act
        var result = Parse(ReadFixture(OnePageFixture));

        // Assert
        // Paid 29.09.2026, booked 30.09.2026.
        var row = result.Transactions[FirstBookedRow];
        Assert.Equal(new DateOnly(2026, 9, 29), row.Date);
        Assert.Equal(-8.99m, row.Amount);
    }

    [Fact]
    public void Parse_ReadsSignedAmounts()
    {
        // Act
        var result = Parse(ReadFixture(OnePageFixture));

        // Assert
        Assert.Contains(result.Transactions, t => t.Amount == 300.00m && t.Description.Contains("Zwrot za bilety"));
        Assert.Contains(result.Transactions, t => t.Amount == 1200.00m);
        Assert.Contains(result.Transactions, t => t.Amount == -420.00m);
    }

    [Fact]
    public void Parse_JoinsEveryLineOfAFiveLineDescriptionWithSingleSpaces()
    {
        // Arrange
        var fiveLines = OnePage[FiveLineDescriptionRow];
        Assert.Equal(5, fiveLines.DescriptionLines.Count);

        // Act
        var result = Parse(ReadFixture(OnePageFixture));

        // Assert
        var description = result.Transactions[FiveLineDescriptionRow].Description;
        Assert.Contains("Wynagrodzenie za wrzesień 2026 - rozliczenie godzin nadliczbowych oraz premii kwartalnej zgodnie z aneksem nr 1", description);
        Assert.DoesNotContain("  ", description);
        Assert.Equal(description.Trim(), description);
    }

    [Fact]
    public void Parse_KeepsPolishDiacritics()
    {
        // Act
        var result = Parse(ReadFixture(OnePageFixture));

        // Assert
        Assert.Contains(result.Transactions, t => t.Description.Contains("Tytuł: Czynsz za wrzesień 2026") && t.Description.Contains("ADAM PRÓBNY"));
        Assert.Contains(result.Transactions, t => t.Description.Contains("FIRMA PRZYKŁADOWA SP. Z O.O."));
        Assert.Contains(result.Transactions, t => t.Description.Contains("Operacja kartą"));
    }

    [Fact]
    public void Parse_ReturnsFortyRowsInOrder_ForMultiPageFixture()
    {
        // Act
        var result = Parse(ReadFixture(MultiPageFixture));

        // Assert
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
            // Act
            var result = Parse(ReadFixture(fixture));

            // Assert
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
        // Act
        var shortExport = Parse(ReadFixture(OnePageFixture));
        var longExport = Parse(ReadFixture(MultiPageFixture));

        // Assert
        Assert.Equal(shortExport.Transactions, longExport.Transactions.Take(shortExport.Transactions.Count));
    }

    [Theory]
    [InlineData(10, 10, null)]
    [InlineData(15, 19, 5)]
    [InlineData(12, 7, 11)]
    [InlineData(17, 19, 15)]
    public void Parse_ReturnsTheSameKeys_WhateverThePageBreaks(int firstPageRows, int rowsPerPage, int? midPageHeaderAfterRows)
    {
        // Arrange
        var layout = new VeloBankPdfLayout
        {
            FirstPageRows = firstPageRows,
            RowsPerPage = rowsPerPage,
            MidPageHeaderAfterRows = midPageHeaderAfterRows,
        };
        var pdf = VeloBankPdfBuilder.Build(VeloBankSampleData.Header, VeloBankSampleData.MultiPageRows, layout);

        // Act
        var result = Parse(pdf);

        // Assert
        Assert.Equal(Expected(VeloBankSampleData.MultiPageRows), result.Transactions);
    }

    [Fact]
    public void Parse_ReadsValuesCenteredInTheRow_AsWellAsTopAligned()
    {
        // Arrange
        var centred = new VeloBankPdfLayout { FirstPageRows = VeloBankSampleData.OnePageRowCount, MidPageHeaderAfterRows = 15, CenterValueCells = true };

        // Act
        var onePage = Parse(VeloBankPdfBuilder.Build(VeloBankSampleData.Header, OnePage, centred));
        var multiPage = Parse(VeloBankPdfBuilder.Build(VeloBankSampleData.Header, VeloBankSampleData.MultiPageRows, new VeloBankPdfLayout { CenterValueCells = true }));

        // Assert
        Assert.Equal(Parse(ReadFixture(OnePageFixture)).Transactions, onePage.Transactions);
        Assert.Equal(Expected(VeloBankSampleData.MultiPageRows), multiPage.Transactions);
    }

    [Fact]
    public void Parse_AssemblesAmountsAndBalancesPrintedWithSeveralThousandsGroups()
    {
        // Arrange
        var rows = OnePage.ToList();
        rows[CardTamperRow] = rows[CardTamperRow] with
        {
            Amount = -1250.40m,
            DescriptionLines = VeloBankSampleData.CardLines(-1250.40m, "PLN", "SKLEP TESTOWY 12"),
        };
        var rebalanced = VeloBankSampleData.WithRunningBalances(rows, 1_234_567.89m);
        Assert.Equal(1_234_567.89m, rebalanced[FirstBookedRow].Balance);

        // Act
        var result = Parse(BuildOnePagePdf(rebalanced));

        // Assert
        Assert.Equal(Expected(rebalanced), result.Transactions);
        Assert.Contains(result.Transactions, t => t.Amount == -1250.40m);
    }

    [Fact]
    public void Parse_IgnoresPendingRowsInTheBalanceChain_WhereverTheyAre()
    {
        // Arrange
        // A pending row in the middle of the booked ones (its balance cell is never read); the
        // oldest row makes room so the page still fits.
        var rows = OnePage.Take(OnePage.Count - 1).ToList();
        rows.Insert(8, OnePage[0] with { BalanceText = "brak" });
        var layout = new VeloBankPdfLayout { FirstPageRows = rows.Count, MidPageHeaderAfterRows = 15 };

        // Act
        var result = Parse(VeloBankPdfBuilder.Build(VeloBankSampleData.Header, rows, layout));

        // Assert
        Assert.Equal(Expected(rows), result.Transactions);
    }

    [Fact]
    public void Parse_ParsesAStatementThatHasOnlyPendingRows()
    {
        // Arrange
        var pending = OnePage.Take(2).ToList();

        // Act
        var result = Parse(VeloBankPdfBuilder.Build(VeloBankSampleData.Header, pending));

        // Assert
        Assert.Equal(Expected(pending), result.Transactions);
        Assert.Equal(0, result.SkippedErrorCount);
    }

    [Fact]
    public void Parse_SkipsForeignCurrencyRow_AndComparesNoBalancesAcrossIt()
    {
        // Arrange
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

        // Act
        var result = Parse(BuildOnePagePdf(rows));

        // Assert
        Assert.Equal(1, result.SkippedErrorCount);
        Assert.Equal(Expected(rows.Where((_, i) => i != ForeignCurrencyRow)), result.Transactions);
        Assert.DoesNotContain(result.Transactions, t => t.Description.Contains("ZAGRANICZNY"));
    }

    [Fact]
    public void Parse_RejectsTheSameBalanceJump_WhenNoForeignCurrencyRowSitsInBetween()
    {
        // Arrange
        var rows = OnePage.ToList();
        for (var i = ForeignCurrencyRow + 1; i < rows.Count; i++)
        {
            rows[i] = rows[i] with { Balance = rows[i].Balance + 15m };
        }

        // Act & Assert
        // The first row below the jump (25.09.2026) is the one whose balance does not follow.
        AssertRejected(BuildOnePagePdf(rows), BalanceCheck, page: 1, transactionDate: "25.09.2026");
    }

    [Fact]
    public void Parse_ImportsPlnCardRowWhoseDescriptionNamesAnotherCurrency_WithoutTheCardAmountCheck()
    {
        // Arrange
        var abroad = OnePage[ForeignCurrencyRow] with
        {
            Amount = -86.40m,
            DescriptionLines = VeloBankSampleData.CardLines(-20m, "EUR", "SKLEP ZAGRANICZNY"),
        };
        var rows = VeloBankSampleData.WithRunningBalances(WithRow(ForeignCurrencyRow, abroad), 1024.50m);

        // Act
        var result = Parse(BuildOnePagePdf(rows));

        // Assert
        Assert.Equal(0, result.SkippedErrorCount);
        Assert.Equal(Expected(rows), result.Transactions);
        Assert.Contains(result.Transactions, t => t.Amount == -86.40m && t.Description.Contains("na kwotę 20,00 EUR"));
    }

    [Fact]
    public void Parse_Rejects_ATamperedBalance()
    {
        // Arrange
        var tampered = OnePage[BalanceTamperRow] with { Balance = OnePage[BalanceTamperRow].Balance + 5m };

        // Act & Assert
        AssertRejected(BuildOnePagePdf(WithRow(BalanceTamperRow, tampered)), BalanceCheck, page: 1, transactionDate: "24.09.2026");
    }

    [Fact]
    public void Parse_Rejects_ATamperedBalanceOnALaterPage_NamingThatPage()
    {
        // Arrange
        var rows = VeloBankSampleData.MultiPageRows.ToList();
        var tampered = rows[20] with { Balance = rows[20].Balance - 1m };
        rows[20] = tampered;

        // Act & Assert
        // Row 21 (index 20) is on page 2 (page 1 holds 15 rows, page 2 the next 19); paid 14.09.2026.
        AssertRejected(VeloBankPdfBuilder.Build(VeloBankSampleData.Header, rows), BalanceCheck, page: 2, transactionDate: "14.09.2026");
    }

    [Fact]
    public void Parse_Rejects_ATamperedCardAmountInTheDescription()
    {
        // Arrange
        var tampered = OnePage[CardTamperRow] with
        {
            DescriptionLines = VeloBankSampleData.CardLines(-35.81m, "PLN", "SKLEP TESTOWY 12"),
        };

        // Act & Assert
        AssertRejected(BuildOnePagePdf(WithRow(CardTamperRow, tampered)), CardAmountCheck, page: 1, transactionDate: "22.09.2026");
    }

    [Fact]
    public void Parse_Rejects_AnUnreadableAmountCell_WithTheRowsDate()
    {
        // Arrange
        var unreadable = OnePage[BalanceTamperRow] with { AmountText = "n/a" };

        // Act & Assert
        AssertRejected(BuildOnePagePdf(WithRow(BalanceTamperRow, unreadable)), UnreadableRowCheck, page: 1, transactionDate: "24.09.2026");
    }

    // Far beyond decimal range: must be an unreadable cell (422), not an OverflowException (500).
    [Theory]
    [InlineData("99999999999999999999999999999,99 PLN")]
    [InlineData("12345678901234567,89 PLN")]
    public void Parse_Rejects_AnAbsurdlyLargeAmountCell_AsUnreadable(string amountText)
    {
        // Arrange
        var unreadable = OnePage[BalanceTamperRow] with { AmountText = amountText };

        // Act & Assert
        AssertRejected(BuildOnePagePdf(WithRow(BalanceTamperRow, unreadable)), UnreadableRowCheck, page: 1, transactionDate: "24.09.2026");
    }

    // Characters that look like part of a Polish amount but are not what the parser accepts: the
    // Unicode minus sign and a dot as the decimal mark. Each must stop the import (the cell is
    // rejected by the number pattern, before any balance check); none may be read as a wrong number.
    [Theory]
    [InlineData("−420,00 PLN")]
    [InlineData("-420.00 PLN")]
    public void Parse_Rejects_AnAmountCellWithALookalikeCharacter_InsteadOfReadingAWrongValue(string amountText)
    {
        // Arrange
        var lookalike = OnePage[BalanceTamperRow] with { AmountText = amountText };

        // Act & Assert
        AssertRejected(BuildOnePagePdf(WithRow(BalanceTamperRow, lookalike)), UnreadableRowCheck, page: 1, transactionDate: "24.09.2026");
    }

    [Theory]
    [InlineData("−1 024,50 PLN")]
    [InlineData("1 024.50 PLN")]
    public void Parse_Rejects_ABalanceCellWithALookalikeCharacter_InsteadOfReadingAWrongValue(string balanceText)
    {
        // Arrange
        var lookalike = OnePage[BalanceTamperRow] with { BalanceText = balanceText };

        // Act & Assert
        AssertRejected(BuildOnePagePdf(WithRow(BalanceTamperRow, lookalike)), UnreadableRowCheck, page: 1, transactionDate: "24.09.2026");
    }

    // A no-break space as the thousands separator cannot be rejected by the number pattern: PdfPig
    // returns it as an ordinary space (the pattern never sees U+00A0), so the cell is read as the
    // value it prints. Pinned so a change in how the text is extracted shows up as a failing test.
    [Fact]
    public void Parse_ReadsANoBreakSpaceThousandsSeparator_AsTheSameValueAsARegularSpace()
    {
        // Arrange
        var nbsp = OnePage[11] with { AmountText = "1 200,00 PLN" };

        // Act
        var result = Parse(BuildOnePagePdf(WithRow(11, nbsp)));

        // Assert
        Assert.Equal(1200.00m, result.Transactions[11].Amount);
        Assert.Equal(new DateOnly(2026, 9, 23), result.Transactions[11].Date);
        Assert.Equal(0, result.SkippedErrorCount);
    }

    // The balance check compares each booked PLN row with the next newer one, so it needs a
    // neighbour on both sides. Control: a row dropped from the middle of the chain is rejected.
    [Fact]
    public void Parse_Rejects_ADroppedRowInTheMiddleOfTheChain()
    {
        // Arrange
        // Row index 9 (25.09.2026, -89,00) removed: the row below it (24.09.2026) no longer follows.
        var rows = OnePage.Where((_, i) => i != 9).ToList();

        // Act & Assert
        AssertRejected(BuildOnePagePdf(rows), BalanceCheck, page: 1, transactionDate: "24.09.2026");
    }

    // Known limitation: there is no row above the newest booked row and none below the oldest, so
    // nothing proves that either end is complete (the statement prints no opening or closing
    // balance). Update this test if VeloBank statements gain an anchor (e.g. a printed opening balance).
    [Fact]
    public void Parse_Accepts_ADroppedNewestBookedRow_KnownLimitation()
    {
        // Arrange
        var rows = OnePage.Where((_, i) => i != FirstBookedRow).ToList();

        // Act
        var result = Parse(BuildOnePagePdf(rows));

        // Assert
        Assert.Equal(16, result.Transactions.Count);
        Assert.Equal(0, result.SkippedErrorCount);
        Assert.Equal(-150.00m, result.Transactions[FirstBookedRow].Amount);
    }

    [Fact]
    public void Parse_Accepts_ADroppedOldestRow_KnownLimitation()
    {
        // Arrange
        var rows = OnePage.Take(OnePage.Count - 1).ToList();

        // Act
        var result = Parse(BuildOnePagePdf(rows));

        // Assert
        Assert.Equal(16, result.Transactions.Count);
        Assert.Equal(0, result.SkippedErrorCount);
        Assert.Equal(-5.40m, result.Transactions[^1].Amount);
    }

    // Known limitation: a pending row prints no balance and sits outside the chain, so a dropped
    // pending row leaves nothing to compare. Update this test if pending rows become verifiable.
    [Fact]
    public void Parse_Accepts_ADroppedPendingRow_KnownLimitation()
    {
        // Arrange
        var rows = OnePage.Where((_, i) => i != 0).ToList();

        // Act
        var result = Parse(BuildOnePagePdf(rows));

        // Assert
        Assert.Equal(16, result.Transactions.Count);
        Assert.Equal(0, result.SkippedErrorCount);
        Assert.Equal(-112.05m, result.Transactions[0].Amount);
    }

    // Known limitation: a foreign-currency row resets the chain (its effect on the PLN balance is
    // unverified), so a booked row dropped right above or right below it cannot be seen. Without the
    // foreign row the same drops are rejected (see the control above). Update these tests if the
    // balance effect of foreign-currency rows becomes verifiable.
    [Theory]
    [InlineData(ForeignCurrencyRow - 1)]
    [InlineData(ForeignCurrencyRow + 1)]
    public void Parse_Accepts_ADroppedRowNextToAForeignCurrencyRow_KnownLimitation(int droppedIndex)
    {
        // Arrange
        var euro = OnePage[ForeignCurrencyRow] with
        {
            Amount = -20m,
            Currency = "EUR",
            DescriptionLines = VeloBankSampleData.CardLines(-20m, "EUR", "SKLEP ZAGRANICZNY"),
            Balance = 500m,
        };
        var withForeignRow = VeloBankSampleData.WithRunningBalances(WithRow(ForeignCurrencyRow, euro), 1024.50m);
        var rows = withForeignRow.Where((_, i) => i != droppedIndex).ToList();

        // Act
        var result = Parse(BuildOnePagePdf(rows));

        // Assert
        // 17 rows - 1 dropped - 1 euro row skipped.
        Assert.Equal(15, result.Transactions.Count);
        Assert.Equal(1, result.SkippedErrorCount);
    }

    // A later page whose table cannot be found would silently lose its rows.
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void Parse_Rejects_ALaterPageWithoutATableHeader_NamingThatPage(int page)
    {
        // Arrange
        var layout = VeloBankSampleData.MultiPageLayout with { OmitHeaderOnPage = page };
        var pdf = VeloBankPdfBuilder.Build(VeloBankSampleData.Header, VeloBankSampleData.MultiPageRows, layout);

        // Act & Assert
        AssertRejected(pdf, UnreadableRowCheck, page, transactionDate: null);
    }

    [Fact]
    public void Parse_Rejects_AnUnreadableTransactionDate_WithoutADate()
    {
        // Arrange
        var unreadable = OnePage[BalanceTamperRow] with { TransactionDateText = "??.??.????" };

        // Act & Assert
        AssertRejected(BuildOnePagePdf(WithRow(BalanceTamperRow, unreadable)), UnreadableRowCheck, page: 1, transactionDate: null);
    }

    [Fact]
    public void Parse_Rejects_AnUnreadableBalanceOnABookedPlnRow()
    {
        // Arrange
        var unreadable = OnePage[BalanceTamperRow] with { BalanceText = "brak" };

        // Act & Assert
        AssertRejected(BuildOnePagePdf(WithRow(BalanceTamperRow, unreadable)), UnreadableRowCheck, page: 1, transactionDate: "24.09.2026");
    }

    [Fact]
    public void Parse_ReturnsNothing_ForPdfThatIsNotAVeloBankStatement()
    {
        // Act
        var result = Parse(BuildTextPdf("Elektroniczne zestawienie operacji", "mBank S.A."));

        // Assert
        Assert.Empty(result.Transactions);
        Assert.Equal(0, result.SkippedErrorCount);
    }

    [Fact]
    public void Parse_ReturnsNothing_ForAVeloBankPageWithoutATable()
    {
        // Act
        var result = Parse(BuildTextPdf("Historia rachunku", "VeloBank S.A.", "DATA TRANSAKCJI DATA KSIĘGOWANIA OPIS KWOTA SALDO"));

        // Assert
        Assert.Empty(result.Transactions);
        Assert.Equal(0, result.SkippedErrorCount);
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
        var parser = new VeloBankPdfParser();
        using var stream = new MemoryStream([.. new byte[7], .. ReadFixture(OnePageFixture)]);
        stream.Position = 7;

        // Act
        var result = parser.Parse(stream);

        // Assert
        Assert.Equal(VeloBankSampleData.OnePageRowCount, result.Transactions.Count);
        Assert.Equal(7, stream.Position);
    }

    [Fact]
    public void CanParseThenParse_WorksOnOneStream()
    {
        // Arrange
        var parser = new VeloBankPdfParser();
        using var stream = new MemoryStream(ReadFixture(MultiPageFixture));

        // Act & Assert
        Assert.True(parser.CanParse(stream));
        var result = parser.Parse(stream);

        // Assert
        Assert.Equal(VeloBankSampleData.MultiPageRowCount, result.Transactions.Count);
    }
}
