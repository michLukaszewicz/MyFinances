using System.Text.RegularExpressions;
using MyFinances.Api.Import;
using MyFinances.Api.Tests.Support;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using Xunit;

namespace MyFinances.Api.Tests;

// Guards the synthetic Erste PDF fixtures and the generator behind them: they must open with
// PdfPig, have the measured table shape, and be rejected by the other banks' parsers.
// Regenerate the committed PDFs (after changing the generator or the sample data) with:
//   REGENERATE_PDF_FIXTURES=1 dotnet test --filter ErstePdfFixtureTests
// and rebuild, so the new files are copied next to the test assembly.
public class ErstePdfFixtureTests
{
    private const string OnePageFixture = "erste-pdf-sample-synthetic.pdf";
    private const string MultiPageFixture = "erste-pdf-sample-synthetic-multipage.pdf";

    private static string FixturePath(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private static byte[] ReadFixture(string name) => File.ReadAllBytes(FixturePath(name));

    // The Fixtures folder of the test project's sources (not the copy next to the test assembly).
    private static string SourceFixturesDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "MyFinances.Api.Tests.csproj")))
                return Path.Combine(dir.FullName, "Fixtures");
        }
        throw new InvalidOperationException("Could not find MyFinances.Api.Tests.csproj above the test output directory.");
    }

    private static List<Page> Pages(PdfDocument document) => document.GetPages().ToList();

    private static List<string> WordTexts(Page page) => page.GetWords().Select(w => w.Text).ToList();

    private static List<PdfRectangle> FilledRectangles(Page page) =>
        page.Paths
            .Where(p => p.IsFilled)
            .Select(p => p.GetBoundingRectangle())
            .Where(r => r.HasValue)
            .Select(r => r!.Value)
            .ToList();

    // Thin pieces of the row separators that sit in the first column.
    private static int CountSeparators(Page page) =>
        FilledRectangles(page).Count(r =>
            r.Height is > 0.5 and < 1.0 &&
            Math.Abs(r.Left - ErstePdfBuilder.ColumnEdges[0]) < 0.5 &&
            Math.Abs(r.Width - (ErstePdfBuilder.ColumnEdges[1] - ErstePdfBuilder.ColumnEdges[0])) < 1.5);

    // Header cells of the first column (15 pt tall).
    private static int CountHeaderBlocks(Page page) =>
        FilledRectangles(page).Count(r =>
            Math.Abs(r.Height - ErstePdfBuilder.HeaderHeight) < 0.1 &&
            Math.Abs(r.Left - ErstePdfBuilder.ColumnEdges[0]) < 0.5 &&
            r.Width > 30);

    // The month token of every date cell: the three-letter word of "28 wrz 2026" inside the date column.
    private static List<string> DateMonthTokens(Page page) =>
        page.GetWords()
            .Where(w => w.BoundingBox.Left < ErstePdfBuilder.ColumnEdges[1] - 40 && Regex.IsMatch(w.Text, @"^\p{L}{3}$"))
            .Select(w => w.Text)
            .ToList();

    // Words "1" and "0xx,xx" that sit side by side on one baseline: a balance such as 1 098,64 split by its thousands space.
    private static bool HasSplitThousandsBalance(Page page)
    {
        var words = page.GetWords().ToList();
        return words.Where(w => w.Text == "1").Any(one =>
            words.Any(next =>
                Regex.IsMatch(next.Text, @"^0\d\d,\d\d$") &&
                Math.Abs(next.Letters[0].StartBaseLine.Y - one.Letters[0].StartBaseLine.Y) < 0.2 &&
                next.BoundingBox.Left - one.BoundingBox.Right is > 0 and < 5));
    }

    [Fact]
    public void RegenerateFixtures_WritesCommittedPdfs_OnlyWhenRequested()
    {
        // Arrange
        if (Environment.GetEnvironmentVariable("REGENERATE_PDF_FIXTURES") != "1")
            return;

        var directory = SourceFixturesDirectory();

        // Act
        File.WriteAllBytes(Path.Combine(directory, OnePageFixture), ErsteSampleData.BuildOnePagePdf());
        File.WriteAllBytes(Path.Combine(directory, MultiPageFixture), ErsteSampleData.BuildMultiPagePdf());
    }

    [Theory]
    [InlineData(OnePageFixture, 1)]
    [InlineData(MultiPageFixture, 3)]
    public void Fixture_OpensWithPdfPig_WithExpectedPageCount(string fileName, int pageCount)
    {
        // Act
        using var document = PdfDocument.Open(ReadFixture(fileName));

        // Assert
        Assert.Equal(pageCount, document.NumberOfPages);
    }

    [Theory]
    [InlineData(OnePageFixture)]
    [InlineData(MultiPageFixture)]
    public void Fixture_CarriesTitleAccountLineHeaderAndFooter_OnEveryPage(string fileName)
    {
        // Act
        using var document = PdfDocument.Open(ReadFixture(fileName));
        var pages = Pages(document);

        // Assert
        for (var i = 0; i < pages.Count; i++)
        {
            var words = WordTexts(pages[i]);
            Assert.Contains("Lista", words);
            Assert.Contains("transakcji", words);
            Assert.Contains("Konto:", words);
            Assert.Contains("Data", words);
            Assert.Contains("operacji", words);
            Assert.Contains("Operacja", words);
            Assert.Contains("Kwota", words);
            Assert.Contains("Saldo", words);
            Assert.Contains("Dokument", words);
            Assert.Contains("dnia:", words);
            Assert.Contains("Strona", words);
            Assert.Contains($"{pages.Count}", words);
            Assert.Contains("paź", words);
        }
    }

    [Fact]
    public void OnePageFixture_HasOneSeparatorPerRow_AndOneHeaderBlock()
    {
        // Act
        using var document = PdfDocument.Open(ReadFixture(OnePageFixture));
        var page = Pages(document).Single();

        // Assert
        Assert.Equal(ErsteSampleData.OnePageRowCount, CountSeparators(page));
        Assert.Equal(1, CountHeaderBlocks(page));
    }

    [Fact]
    public void MultiPageFixture_HasOneSeparatorPerRow_AndOneHeaderBlockPerPage()
    {
        // Act
        using var document = PdfDocument.Open(ReadFixture(MultiPageFixture));
        var pages = Pages(document);

        // Assert
        Assert.Equal(ErsteSampleData.MultiPageRowCount, pages.Sum(CountSeparators));
        Assert.All(pages, page => Assert.Equal(1, CountHeaderBlocks(page)));
        Assert.Equal(new[] { 15, 15, 10 }, pages.Select(CountSeparators).ToArray());
    }

    [Theory]
    [InlineData(OnePageFixture)]
    [InlineData(MultiPageFixture)]
    public void Fixture_PrintsAnEmptyBookingDateLabelUnderEveryDate(string fileName)
    {
        // Act
        using var document = PdfDocument.Open(ReadFixture(fileName));

        // Assert
        foreach (var page in Pages(document))
        {
            var words = WordTexts(page);
            Assert.Equal(CountSeparators(page), words.Count(w => w == "księgowania"));
            Assert.Equal(CountSeparators(page), DateMonthTokens(page).Count);
        }
    }

    [Theory]
    [InlineData(OnePageFixture)]
    [InlineData(MultiPageFixture)]
    public void Fixture_PrintsPolishMonthAbbreviations(string fileName)
    {
        // Act
        using var document = PdfDocument.Open(ReadFixture(fileName));
        var months = Pages(document).SelectMany(DateMonthTokens).ToList();

        // Assert
        Assert.NotEmpty(months);
        Assert.All(months, m => Assert.Contains(m, ErstePdfBuilder.MonthAbbreviations));
        Assert.Contains("paź", months);
        Assert.Contains("wrz", months);
    }

    [Theory]
    [InlineData(OnePageFixture)]
    [InlineData(MultiPageFixture)]
    public void Fixture_PrintsThousandsBalanceAsTwoWords(string fileName)
    {
        // Act
        using var document = PdfDocument.Open(ReadFixture(fileName));
        var pages = Pages(document);

        // Assert
        Assert.Contains(pages, HasSplitThousandsBalance);
        Assert.DoesNotContain(pages, page => page.GetWords().Any(w => Regex.IsMatch(w.Text, @"^-?\d{4},\d{2}$")));
    }

    [Theory]
    [InlineData(OnePageFixture)]
    [InlineData(MultiPageFixture)]
    public void Fixture_MatchesWhatTheGeneratorProducesNow(string fileName)
    {
        // Arrange
        var regenerated = fileName == OnePageFixture ? ErsteSampleData.BuildOnePagePdf() : ErsteSampleData.BuildMultiPagePdf();

        // Act
        using var committed = PdfDocument.Open(ReadFixture(fileName));
        using var fresh = PdfDocument.Open(regenerated);

        // Assert
        Assert.Equal(
            fresh.GetPages().Select(p => p.Text).ToList(),
            committed.GetPages().Select(p => p.Text).ToList());
    }

    [Theory]
    [InlineData(OnePageFixture)]
    [InlineData(MultiPageFixture)]
    public void Fixture_IsDetectedAsPdf(string fileName)
    {
        // Arrange
        using var stream = new MemoryStream(ReadFixture(fileName));

        // Act & Assert
        Assert.Equal(StatementFormat.Pdf, StatementFormatSniffer.Detect(stream));
    }

    [Theory]
    [InlineData(OnePageFixture)]
    [InlineData(MultiPageFixture)]
    public void OtherParsers_RejectPdfFixturesWithoutThrowing(string fileName)
    {
        // Arrange
        using var forMBankCsv = new MemoryStream(ReadFixture(fileName));
        using var forErsteCsv = new MemoryStream(ReadFixture(fileName));
        using var forMBankPdf = new MemoryStream(ReadFixture(fileName));
        using var forVeloBankPdf = new MemoryStream(ReadFixture(fileName));

        // Act & Assert
        Assert.False(new MBankCsvParser().CanParse(forMBankCsv));
        Assert.False(new ErsteCsvParser().CanParse(forErsteCsv));
        Assert.False(new MBankPdfParser().CanParse(forMBankPdf));
        Assert.False(new VeloBankPdfParser().CanParse(forVeloBankPdf));
    }

    [Fact]
    public void Generator_IsDeterministic()
    {
        // Act & Assert
        Assert.Equal(ErsteSampleData.BuildOnePagePdf(), ErsteSampleData.BuildOnePagePdf());
        Assert.Equal(ErsteSampleData.BuildMultiPagePdf(), ErsteSampleData.BuildMultiPagePdf());
    }

    [Fact]
    public void Generator_SplitsRowsOverPagesAccordingToTheLayout()
    {
        // Arrange
        var layout = new ErstePdfLayout { FirstPageRows = 10, RowsPerPage = 10 };

        // Act
        using var document = PdfDocument.Open(
            ErstePdfBuilder.Build(ErsteSampleData.Header, ErsteSampleData.MultiPageRows, layout));

        // Assert
        Assert.Equal(new[] { 10, 10, 10, 10 }, Pages(document).Select(CountSeparators).ToArray());
    }

    [Fact]
    public void Generator_WritesRawTextOverridesVerbatim()
    {
        // Arrange
        var row = ErsteSampleData.OnePageRows[2] with { AmountText = "n/a", DateText = "?? ??? ????", BalanceText = "brak" };

        // Act
        using var document = PdfDocument.Open(ErstePdfBuilder.Build(ErsteSampleData.Header, [row]));
        var words = WordTexts(Pages(document).Single());

        // Assert
        Assert.Contains("n/a", words);
        Assert.Contains("???", words);
        Assert.Contains("brak", words);
    }

    [Fact]
    public void Generator_RefusesRowsThatWouldRunIntoTheFooter()
    {
        // Arrange
        var layout = new ErstePdfLayout { FirstPageRows = 40 };

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            ErstePdfBuilder.Build(ErsteSampleData.Header, ErsteSampleData.MultiPageRows, layout));
    }

    [Fact]
    public void Generator_CanLeaveOutTheHeaderCellsOfOnePage_WithoutMovingItsRows()
    {
        // Arrange
        var withHeaders = ErstePdfBuilder.Build(ErsteSampleData.Header, ErsteSampleData.MultiPageRows, ErsteSampleData.MultiPageLayout);
        var layout = ErsteSampleData.MultiPageLayout with { OmitHeaderOnPage = 2 };
        var withoutHeader = ErstePdfBuilder.Build(ErsteSampleData.Header, ErsteSampleData.MultiPageRows, layout);

        // Act
        using var full = PdfDocument.Open(withHeaders);
        using var cut = PdfDocument.Open(withoutHeader);
        var fullPages = Pages(full);
        var cutPages = Pages(cut);

        // Assert
        Assert.Equal(new[] { 1, 0, 1 }, cutPages.Select(CountHeaderBlocks).ToArray());
        Assert.DoesNotContain("Operacja", WordTexts(cutPages[1]));
        Assert.Equal(CountSeparators(fullPages[1]), CountSeparators(cutPages[1]));
        Assert.Equal(
            fullPages[1].GetWords().First(w => w.Text == "księgowania").BoundingBox,
            cutPages[1].GetWords().First(w => w.Text == "księgowania").BoundingBox);
    }

    [Fact]
    public void Generator_CanLeaveOutTheAccountLineAndTheTitle()
    {
        // Arrange
        var layout = new ErstePdfLayout { FirstPageRows = ErsteSampleData.OnePageRowCount, OmitAccountLine = true, OmitTitle = true };

        // Act
        using var document = PdfDocument.Open(ErstePdfBuilder.Build(ErsteSampleData.Header, ErsteSampleData.OnePageRows, layout));
        var words = WordTexts(Pages(document).Single());

        // Assert
        Assert.DoesNotContain("Konto:", words);
        Assert.DoesNotContain("Lista", words);
        Assert.DoesNotContain("transakcji", words);
        Assert.Contains("Operacja", words);
    }

    [Fact]
    public void SampleData_OnePageRows_MirrorTheRealShape()
    {
        // Arrange
        var rows = ErsteSampleData.OnePageRows;

        // Assert
        Assert.Equal(ErsteSampleData.OnePageRowCount, rows.Count);
        Assert.All(rows, r => Assert.Equal("PLN", r.Currency));
        Assert.Contains(rows, r => r.OperationLines.Count == 2 && r.Amount > 0);
        Assert.Contains(rows, r => r.Balance >= 1000m);
        Assert.Contains(rows, r => r.OperationDate.Month == 10);
        Assert.Contains(rows, r => r.OperationDate.Month == 9);
        Assert.Equal(rows, ErsteSampleData.MultiPageRows.Take(ErsteSampleData.OnePageRowCount));

        // A group of rows sharing date, amount and description.
        Assert.Contains(
            rows.GroupBy(r => (r.OperationDate, r.Amount, Description: string.Join(' ', r.OperationLines))),
            g => g.Count() >= 3);
    }

    [Fact]
    public void SampleData_MultiPageRows_HaveFortyRows()
    {
        // Act & Assert
        Assert.Equal(ErsteSampleData.MultiPageRowCount, ErsteSampleData.MultiPageRows.Count);
        Assert.Equal(ErsteSampleData.MultiPageRowCount, ErsteSampleData.MultiPageRowsInBookingOrder.Count);
        Assert.Equal(ErsteSampleData.NewestBalance, ErsteSampleData.MultiPageRowsInBookingOrder[^1].Balance);
    }

    [Fact]
    public void SampleData_BookingOrder_FormsOneExactBalanceChain()
    {
        // Arrange
        var rows = ErsteSampleData.MultiPageRowsInBookingOrder;

        // Assert
        Assert.Equal(rows[0].Balance, ErsteSampleData.OpeningBalance + rows[0].Amount);
        for (var i = 1; i < rows.Count; i++)
            Assert.Equal(rows[i - 1].Balance + rows[i].Amount, rows[i].Balance);
        Assert.All(rows, r => Assert.True(r.Balance > 0));
    }

    [Fact]
    public void SampleData_PrintedOrder_DoesNotFollowTheBalanceChain_ButTheLinkageIsOrderIndependent()
    {
        // Arrange
        var rows = ErsteSampleData.MultiPageRows;

        // Act
        // Printed newest first, a row's balance before equals the next printed row's balance after - except
        // where same-day rows are printed out of booking order.
        var breaks = Enumerable.Range(0, rows.Count - 1).Count(i => rows[i].Balance - rows[i].Amount != rows[i + 1].Balance);

        // Assert
        Assert.InRange(breaks, 3, rows.Count / 2);

        AssertOrderIndependentLinkage(rows);
    }

    [Fact]
    public void SampleData_OnePageRows_KeepACompleteChainOfTheirOwn()
    {
        // Act & Assert
        // The cut between row 12 and 13 falls between two booking days, so the prefix is one contiguous
        // stretch of the booking order and its linkage holds on its own.
        AssertOrderIndependentLinkage(ErsteSampleData.OnePageRows);
    }

    // The multiset of "balance before" (balance - amount) equals the multiset of "balance after" with
    // exactly one value unmatched on each side: the oldest row's before-balance and the newest row's after-balance.
    private static void AssertOrderIndependentLinkage(IReadOnlyList<ErstePdfRow> rows)
    {
        var before = rows.Select(r => r.Balance - r.Amount).ToList();
        var after = rows.Select(r => r.Balance).ToList();

        foreach (var value in before.ToList())
        {
            if (after.Remove(value))
                before.Remove(value);
        }

        Assert.Single(before);
        Assert.Single(after);
    }
}
