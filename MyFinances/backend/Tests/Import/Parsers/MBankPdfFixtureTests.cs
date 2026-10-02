using System.Text.RegularExpressions;
using MyFinances.Api.Import;
using MyFinances.Api.Tests.Support;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using Xunit;

namespace MyFinances.Api.Tests.Import.Parsers;

// Guards the synthetic mBank PDF fixtures and the generator behind them: they must open with
// PdfPig, have the measured table shape, and be rejected by the other banks' parsers.
// Regenerate the committed PDFs (after changing the generator or the sample data) with:
//   REGENERATE_PDF_FIXTURES=1 dotnet test --filter MBankPdfFixtureTests
// and rebuild, so the new files are copied next to the test assembly.
public class MBankPdfFixtureTests
{
    private const string TwoPageFixture = "mbank-pdf-sample-synthetic.pdf";
    private const string MultiPageFixture = "mbank-pdf-sample-synthetic-multipage.pdf";

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

    // Date lines of the table: an ISO date in the first (booking date) column.
    private static int CountDateLines(Page page) =>
        page.GetWords().Count(w =>
            Regex.IsMatch(w.Text, @"^\d{4}-\d{2}-\d{2}$") &&
            Math.Abs(w.BoundingBox.Left - 48.1) < 1.0);

    // Words "3" and "473,70" that sit side by side on one baseline: a balance such as 3 473,70 split by its thousands space.
    private static bool HasSplitThousandsBalance(Page page)
    {
        var words = page.GetWords().ToList();
        return words.Where(w => Regex.IsMatch(w.Text, @"^\d{1,3}$")).Any(thousands =>
            words.Any(next =>
                Regex.IsMatch(next.Text, @"^\d{3},\d\d$") &&
                Math.Abs(next.Letters[0].StartBaseLine.Y - thousands.Letters[0].StartBaseLine.Y) < 0.2 &&
                next.BoundingBox.Left - thousands.BoundingBox.Right is > 0 and < 3));
    }

    // Horizontal stroked grid segments that span the first (booking date) column.
    private static int CountFirstColumnHorizontalLines(Page page) =>
        page.Paths.Count(p =>
        {
            if (!p.IsStroked || p.IsFilled)
                return false;
            var r = p.GetBoundingRectangle();
            return r.HasValue &&
                r.Value.Height < 0.01 &&
                Math.Abs(r.Value.Left - MBankPdfBuilder.ColumnEdges[0]) < 0.1 &&
                Math.Abs(r.Value.Right - MBankPdfBuilder.ColumnEdges[1]) < 0.1;
        });

    [Fact]
    public void RegenerateFixtures_WritesCommittedPdfs_OnlyWhenRequested()
    {
        // Arrange
        if (Environment.GetEnvironmentVariable("REGENERATE_PDF_FIXTURES") != "1")
            return;

        var directory = SourceFixturesDirectory();

        // Act
        File.WriteAllBytes(Path.Combine(directory, TwoPageFixture), MBankSampleData.BuildTwoPagePdf());
        File.WriteAllBytes(Path.Combine(directory, MultiPageFixture), MBankSampleData.BuildMultiPagePdf());
    }

    [Theory]
    [InlineData(TwoPageFixture, 2)]
    [InlineData(MultiPageFixture, 3)]
    public void Fixture_OpensWithPdfPig_WithExpectedPageCount(string fileName, int pageCount)
    {
        // Act
        using var document = PdfDocument.Open(ReadFixture(fileName));

        // Assert
        Assert.Equal(pageCount, document.NumberOfPages);
    }

    [Theory]
    [InlineData(TwoPageFixture)]
    [InlineData(MultiPageFixture)]
    public void Fixture_CarriesTitleOnFirstPage_AndTableHeaderAndFooterOnEveryPage(string fileName)
    {
        // Act
        using var document = PdfDocument.Open(ReadFixture(fileName));
        var pages = Pages(document);

        // Assert
        var firstPageWords = WordTexts(pages[0]);
        Assert.Contains("Elektroniczne", firstPageWords);
        Assert.Contains("zestawienie", firstPageWords);
        Assert.Contains("operacji", firstPageWords);
        Assert.Contains("Waluta", firstPageWords);
        Assert.Contains("początkowe:", firstPageWords);
        Assert.Contains("Podsumowanie", firstPageWords);

        for (var i = 0; i < pages.Count; i++)
        {
            var words = WordTexts(pages[i]);
            Assert.Contains("księgowania", words);
            Assert.Contains("Opis", words);
            Assert.Contains("Kwota", words);
            Assert.Contains("Saldo", words);
            Assert.Contains("Strona", words);
            Assert.Contains($"{pages.Count}", words);
        }

        Assert.Contains("końcowe:", WordTexts(pages[^1]));
        Assert.DoesNotContain("końcowe:", pages.Take(pages.Count - 1).SelectMany(WordTexts));
    }

    [Fact]
    public void TwoPageFixture_HasOneDateLinePerRow()
    {
        // Act
        using var document = PdfDocument.Open(ReadFixture(TwoPageFixture));
        var pages = Pages(document);

        // Assert
        Assert.Equal(MBankSampleData.TwoPageRowCount, pages.Sum(CountDateLines));
        Assert.Equal(new[] { 12, 8 }, pages.Select(CountDateLines).ToArray());
    }

    [Fact]
    public void MultiPageFixture_HasOneDateLinePerRow_AndRepeatsHeaderAndFooterOnEveryPage()
    {
        // Act
        using var document = PdfDocument.Open(ReadFixture(MultiPageFixture));
        var pages = Pages(document);

        // Assert
        Assert.Equal(MBankSampleData.MultiPageRowCount, pages.Sum(CountDateLines));
        Assert.Equal(new[] { 12, 14, 14 }, pages.Select(CountDateLines).ToArray());
        Assert.All(pages, page => Assert.Equal(1, WordTexts(page).Count(w => w == "Strona")));
    }

    [Theory]
    [InlineData(TwoPageFixture)]
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
    [InlineData(TwoPageFixture)]
    [InlineData(MultiPageFixture)]
    public void Fixture_DrawsTheGridAsStrokedLines_NotAsFilledRectangles(string fileName)
    {
        // Act
        using var document = PdfDocument.Open(ReadFixture(fileName));
        var pages = Pages(document);

        // Assert
        Assert.All(pages, page => Assert.DoesNotContain(page.Paths, p => p.IsFilled));
        Assert.All(pages, page => Assert.True(CountFirstColumnHorizontalLines(page) > 2 * CountDateLines(page)));
    }

    [Theory]
    [InlineData(TwoPageFixture)]
    [InlineData(MultiPageFixture)]
    public void Fixture_MatchesWhatTheGeneratorProducesNow(string fileName)
    {
        // Arrange
        var regenerated = fileName == TwoPageFixture ? MBankSampleData.BuildTwoPagePdf() : MBankSampleData.BuildMultiPagePdf();

        // Act
        using var committed = PdfDocument.Open(ReadFixture(fileName));
        using var fresh = PdfDocument.Open(regenerated);

        // Assert
        Assert.Equal(
            fresh.GetPages().Select(p => p.Text).ToList(),
            committed.GetPages().Select(p => p.Text).ToList());
    }

    [Theory]
    [InlineData(TwoPageFixture)]
    [InlineData(MultiPageFixture)]
    public void Fixture_IsDetectedAsPdf(string fileName)
    {
        // Arrange
        using var stream = new MemoryStream(ReadFixture(fileName));

        // Act & Assert
        Assert.Equal(StatementFormat.Pdf, StatementFormatSniffer.Detect(stream));
    }

    [Theory]
    [InlineData(TwoPageFixture)]
    [InlineData(MultiPageFixture)]
    public void OtherParsers_RejectPdfFixturesWithoutThrowing(string fileName)
    {
        // Arrange
        using var forMBankCsv = new MemoryStream(ReadFixture(fileName));
        using var forErsteCsv = new MemoryStream(ReadFixture(fileName));
        using var forVeloBankPdf = new MemoryStream(ReadFixture(fileName));

        // Act & Assert
        Assert.False(new MBankCsvParser().CanParse(forMBankCsv));
        Assert.False(new ErsteCsvParser().CanParse(forErsteCsv));
        Assert.False(new VeloBankPdfParser().CanParse(forVeloBankPdf));
    }

    [Fact]
    public void Generator_IsDeterministic()
    {
        // Act & Assert
        Assert.Equal(MBankSampleData.BuildTwoPagePdf(), MBankSampleData.BuildTwoPagePdf());
        Assert.Equal(MBankSampleData.BuildMultiPagePdf(), MBankSampleData.BuildMultiPagePdf());
    }

    [Fact]
    public void Generator_SplitsRowsOverPagesAccordingToTheLayout()
    {
        // Arrange
        var layout = new MBankPdfLayout { FirstPageRows = 10, RowsPerPage = 10 };

        // Act
        using var document = PdfDocument.Open(
            MBankPdfBuilder.Build(MBankSampleData.Header, MBankSampleData.OpeningBalance, MBankSampleData.MultiPageRows, layout));

        // Assert
        Assert.Equal(new[] { 10, 10, 10, 10 }, Pages(document).Select(CountDateLines).ToArray());
    }

    [Fact]
    public void Generator_WritesRawTextOverridesVerbatim()
    {
        // Arrange
        var row = MBankSampleData.TwoPageRows[2] with { AmountText = "n/a", BookingDateText = "??-??-????", BalanceText = "brak" };

        // Act
        using var document = PdfDocument.Open(
            MBankPdfBuilder.Build(MBankSampleData.Header, MBankSampleData.OpeningBalance, [row]));
        var words = WordTexts(Pages(document).Single());

        // Assert
        Assert.Contains("n/a", words);
        Assert.Contains("??-??-????", words);
        Assert.Contains("brak", words);
    }

    [Fact]
    public void Generator_RefusesRowsThatWouldRunIntoTheFooter()
    {
        // Arrange
        var layout = new MBankPdfLayout { FirstPageRows = 40 };

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            MBankPdfBuilder.Build(MBankSampleData.Header, MBankSampleData.OpeningBalance, MBankSampleData.MultiPageRows, layout));
    }

    [Fact]
    public void Generator_PrintsSummaryAndClosingBalanceComputedFromTheRows()
    {
        // Arrange
        var rows = MBankSampleData.TwoPageRows;
        var summary = MBankPdfBuilder.ComputeSummary(rows);

        // Act
        using var document = PdfDocument.Open(MBankSampleData.BuildTwoPagePdf());
        var pages = Pages(document);

        // Assert
        Assert.Equal(rows.Count(r => r.Amount > 0), summary.CreditCount);
        Assert.Equal(rows.Count(r => r.Amount < 0), summary.DebitCount);
        Assert.Equal(MBankSampleData.TwoPageRowCount, summary.CreditCount + summary.DebitCount);
        Assert.Contains(MBankPdfBuilder.FormatMoney(summary.CreditSum).Split(' ')[^1], WordTexts(pages[0]));
        Assert.Contains(MBankPdfBuilder.FormatMoney(rows[^1].Balance).Split(' ')[^1], WordTexts(pages[^1]));
    }

    [Fact]
    public void Generator_AppliesSummaryAndClosingBalanceOverridesAndOmissions()
    {
        // Arrange
        var rows = MBankSampleData.TwoPageRows;
        var layout = MBankSampleData.TwoPageLayout with
        {
            SummaryOverride = new MBankPdfSummary(1, 11.11m, 2, 22.22m),
            ClosingBalanceOverride = 77.77m,
            OmitOpeningBalance = true,
        };

        // Act
        using var tampered = PdfDocument.Open(MBankPdfBuilder.Build(MBankSampleData.Header, MBankSampleData.OpeningBalance, rows, layout));
        var tamperedPages = Pages(tampered);

        // Assert
        Assert.Contains("11,11", WordTexts(tamperedPages[0]));
        Assert.Contains("22,22", WordTexts(tamperedPages[0]));
        Assert.Contains("77,77", WordTexts(tamperedPages[^1]));
        Assert.DoesNotContain("początkowe:", WordTexts(tamperedPages[0]));

        // Arrange
        var omitted = MBankSampleData.TwoPageLayout with { OmitClosingBalance = true, OmitSummary = true };

        // Act
        using var bare = PdfDocument.Open(MBankPdfBuilder.Build(MBankSampleData.Header, MBankSampleData.OpeningBalance, rows, omitted));
        var barePages = Pages(bare);

        // Assert
        Assert.DoesNotContain("końcowe:", WordTexts(barePages[^1]));
        Assert.DoesNotContain("Podsumowanie", WordTexts(barePages[0]));
    }

    [Fact]
    public void SampleData_TwoPageRows_MirrorTheRealShape()
    {
        // Arrange
        var rows = MBankSampleData.TwoPageRows;

        // Assert
        Assert.Equal(MBankSampleData.TwoPageRowCount, rows.Count);
        Assert.Contains(rows, r => r.DescriptionLines[0] == "ZAKUP PRZY UŻYCIU KARTY" && r.DescriptionLines[1].Contains("DATA TRANSAKCJI:"));
        Assert.Contains(rows, r => r.DescriptionLines[0].StartsWith("BLIK"));
        Assert.Contains(rows, r => r.DescriptionLines[0] == "POS ZWROT TOWARU" && r.Amount > 0);
        Assert.Contains(rows, r => r.DescriptionLines[0] == "PRZELEW ZEWNĘTRZNY PRZYCHODZĄCY" && r.DescriptionLines.Count >= 5);
        Assert.Contains(rows, r => r.Balance >= 1000m);
        Assert.Contains(rows, r => r.Amount > 0);
        Assert.Contains(rows, r => r.Amount < 0);
        Assert.Equal(rows, MBankSampleData.MultiPageRows.Take(MBankSampleData.TwoPageRowCount));
        Assert.Equal(rows.OrderBy(r => r.BookingDate).ToList(), rows);
    }

    [Fact]
    public void SampleData_MultiPageRows_HaveFortyRows_AndBalancesFormOneExactChain()
    {
        // Arrange
        var rows = MBankSampleData.MultiPageRows;

        // Assert
        Assert.Equal(MBankSampleData.MultiPageRowCount, rows.Count);
        Assert.Equal(rows[0].Balance, MBankSampleData.OpeningBalance + rows[0].Amount);
        for (var i = 1; i < rows.Count; i++)
            Assert.Equal(rows[i - 1].Balance + rows[i].Amount, rows[i].Balance);
        Assert.All(rows, r => Assert.True(r.Balance > 0));
    }

    [Fact]
    public void SampleData_PagesFitTheirRows_WithTwoLineRowsOnPageOne()
    {
        // Act & Assert
        Assert.All(MBankSampleData.TwoPageRows.Take(MBankSampleData.TwoPageLayout.FirstPageRows),
            r => Assert.Equal(2, r.DescriptionLines.Count));
    }
}
