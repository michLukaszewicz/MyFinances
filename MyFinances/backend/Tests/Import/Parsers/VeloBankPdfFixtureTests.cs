using System.Text.RegularExpressions;
using MyFinances.Api.Import;
using MyFinances.Api.Tests.Support;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using Xunit;

namespace MyFinances.Api.Tests;

// Guards the synthetic VeloBank PDF fixtures and the generator behind them: they must open with
// PdfPig, have the measured table shape, and be rejected by the CSV parsers.
// Regenerate the committed PDFs (after changing the generator or the sample data) with:
//   REGENERATE_PDF_FIXTURES=1 dotnet test --filter VeloBankPdfFixtureTests
// and rebuild, so the new files are copied next to the test assembly.
public class VeloBankPdfFixtureTests
{
    private const string OnePageFixture = "velobank-sample-synthetic.pdf";
    private const string MultiPageFixture = "velobank-sample-synthetic-multipage.pdf";

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
            r.Height is > 1.0 and < 1.4 &&
            Math.Abs(r.Left - VeloBankPdfBuilder.ColumnEdges[0]) < 0.5 &&
            Math.Abs(r.Width - (VeloBankPdfBuilder.ColumnEdges[1] - VeloBankPdfBuilder.ColumnEdges[0])) < 1.5);

    // Header cells of the first column (28 pt tall).
    private static int CountHeaderBlocks(Page page) =>
        FilledRectangles(page).Count(r =>
            Math.Abs(r.Height - VeloBankPdfBuilder.HeaderHeight) < 0.1 &&
            Math.Abs(r.Left - VeloBankPdfBuilder.ColumnEdges[0]) < 0.5 &&
            r.Width > 30);

    // Words "1" and "0xx,xx" that sit side by side on one baseline: a balance such as 1 024,50 split by its thousands space.
    private static bool HasSplitThousandsBalance(Page page)
    {
        var words = page.GetWords().ToList();
        return words.Where(w => w.Text == "1").Any(one =>
            words.Any(next =>
                Regex.IsMatch(next.Text, @"^0\d\d,\d\d$") &&
                Math.Abs(next.Letters[0].StartBaseLine.Y - one.Letters[0].StartBaseLine.Y) < 0.2 &&
                next.BoundingBox.Left - one.BoundingBox.Right is > 0 and < 3));
    }

    [Fact]
    public void RegenerateFixtures_WritesCommittedPdfs_OnlyWhenRequested()
    {
        // Arrange
        if (Environment.GetEnvironmentVariable("REGENERATE_PDF_FIXTURES") != "1")
            return;

        var directory = SourceFixturesDirectory();

        // Act
        File.WriteAllBytes(Path.Combine(directory, OnePageFixture), VeloBankSampleData.BuildOnePagePdf());
        File.WriteAllBytes(Path.Combine(directory, MultiPageFixture), VeloBankSampleData.BuildMultiPagePdf());
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
    public void Fixture_CarriesPolishHeaderText_OnEveryPage(string fileName)
    {
        // Act
        using var document = PdfDocument.Open(ReadFixture(fileName));

        // Assert
        foreach (var page in Pages(document))
        {
            var words = page.GetWords().Select(w => w.Text).ToList();
            Assert.Contains("KSIĘGOWANIA", words);
            Assert.Contains("TRANSAKCJI", words);
        }

        Assert.Contains("Historia", Pages(document)[0].GetWords().Select(w => w.Text));
    }

    [Fact]
    public void OnePageFixture_HasOneSeparatorPerRow_AndARepeatedHeaderBlock()
    {
        // Act
        using var document = PdfDocument.Open(ReadFixture(OnePageFixture));
        var page = Pages(document).Single();

        // Assert
        Assert.Equal(VeloBankSampleData.OnePageRowCount, CountSeparators(page));
        Assert.Equal(2, CountHeaderBlocks(page));
    }

    [Fact]
    public void MultiPageFixture_HasOneSeparatorPerRow_AndOneHeaderBlockPerPage()
    {
        // Act
        using var document = PdfDocument.Open(ReadFixture(MultiPageFixture));
        var pages = Pages(document);

        // Assert
        Assert.Equal(VeloBankSampleData.MultiPageRowCount, pages.Sum(CountSeparators));
        Assert.All(pages, page => Assert.Equal(1, CountHeaderBlocks(page)));
        Assert.Equal(new[] { 15, 19, 6 }, pages.Select(CountSeparators).ToArray());
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

    [Fact]
    public void OnePageFixture_PrintsDashForEachPendingBookingDateAndBalance()
    {
        // Act
        using var document = PdfDocument.Open(ReadFixture(OnePageFixture));
        var dashes = Pages(document).Single().GetWords().Where(w => w.Text == "-").ToList();

        var bookingColumn = dashes.Count(w => w.BoundingBox.Left < VeloBankPdfBuilder.ColumnEdges[2] && w.BoundingBox.Left > VeloBankPdfBuilder.ColumnEdges[1]);
        var balanceColumn = dashes.Count(w => w.BoundingBox.Left > VeloBankPdfBuilder.ColumnEdges[4]);

        // Assert
        Assert.Equal(2, bookingColumn);
        Assert.Equal(2, balanceColumn);
    }

    [Theory]
    [InlineData(OnePageFixture)]
    [InlineData(MultiPageFixture)]
    public void Fixture_MatchesWhatTheGeneratorProducesNow(string fileName)
    {
        // Arrange
        var regenerated = fileName == OnePageFixture ? VeloBankSampleData.BuildOnePagePdf() : VeloBankSampleData.BuildMultiPagePdf();

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
    public void CsvParsers_RejectPdfFixturesWithoutThrowing(string fileName)
    {
        // Arrange
        using var forMBank = new MemoryStream(ReadFixture(fileName));
        using var forErste = new MemoryStream(ReadFixture(fileName));

        // Act & Assert
        Assert.False(new MBankCsvParser().CanParse(forMBank));
        Assert.False(new ErsteCsvParser().CanParse(forErste));
    }

    [Fact]
    public void Generator_IsDeterministic()
    {
        // Act & Assert
        Assert.Equal(VeloBankSampleData.BuildOnePagePdf(), VeloBankSampleData.BuildOnePagePdf());
        Assert.Equal(VeloBankSampleData.BuildMultiPagePdf(), VeloBankSampleData.BuildMultiPagePdf());
    }

    [Fact]
    public void Generator_SplitsRowsOverPagesAccordingToTheLayout()
    {
        // Arrange
        var layout = new VeloBankPdfLayout { FirstPageRows = 10, RowsPerPage = 10 };

        // Act
        using var document = PdfDocument.Open(VeloBankPdfBuilder.Build(VeloBankSampleData.Header, VeloBankSampleData.MultiPageRows, layout));

        // Assert
        Assert.Equal(new[] { 10, 10, 10, 10 }, Pages(document).Select(CountSeparators).ToArray());
    }

    [Fact]
    public void Generator_WritesRawTextOverridesVerbatim()
    {
        // Arrange
        var row = VeloBankSampleData.OnePageRows[2] with { AmountText = "n/a", TransactionDateText = "??.??.????", BalanceText = "brak" };

        // Act
        using var document = PdfDocument.Open(VeloBankPdfBuilder.Build(VeloBankSampleData.Header, [row]));
        var words = Pages(document).Single().GetWords().Select(w => w.Text).ToList();

        // Assert
        Assert.Contains("n/a", words);
        Assert.Contains("??.??.????", words);
        Assert.Contains("brak", words);
    }

    [Fact]
    public void Generator_RefusesRowsThatWouldRunIntoTheFooter()
    {
        // Arrange
        var layout = new VeloBankPdfLayout { FirstPageRows = 40 };

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            VeloBankPdfBuilder.Build(VeloBankSampleData.Header, VeloBankSampleData.MultiPageRows, layout));
    }

    [Fact]
    public void Generator_KeepsValueCellsBesideTheFirstLine_UnlessCenteringIsRequested()
    {
        // Arrange
        var tallRow = VeloBankSampleData.MultiPageRows.First(r => r.DescriptionLines.Count == 5);

        double Gap(VeloBankPdfLayout layout)
        {
            using var document = PdfDocument.Open(VeloBankPdfBuilder.Build(VeloBankSampleData.Header, [tallRow], layout));
            var words = Pages(document).Single().GetWords().ToList();
            var firstLine = words.First(w => w.Text == "Przelew").Letters[0].StartBaseLine.Y;
            var dateBaseline = words.First(w => Regex.IsMatch(w.Text, @"^\d\d\.\d\d\.\d{4}$")).Letters[0].StartBaseLine.Y;
            return firstLine - dateBaseline;
        }

        // Act & Assert
        Assert.InRange(Gap(new VeloBankPdfLayout()), 0, 2);
        Assert.InRange(Gap(new VeloBankPdfLayout { CenterValueCells = true }), 8, 14);
    }

    [Fact]
    public void SampleData_OnePageRows_MirrorTheNinetyDayExport()
    {
        // Arrange
        var rows = VeloBankSampleData.OnePageRows;

        // Assert
        Assert.Equal(VeloBankSampleData.OnePageRowCount, rows.Count);
        Assert.Equal(new[] { true, true }, rows.Take(2).Select(r => r.BookingDate is null && r.Balance is null));
        Assert.All(rows.Skip(2), r => Assert.True(r.BookingDate is not null && r.Balance is not null));
        Assert.All(rows, r => Assert.InRange(r.DescriptionLines.Count, 1, 5));
        Assert.Contains(rows, r => r.DescriptionLines[0].StartsWith("Operacja kartą"));
        Assert.Contains(rows, r => r.DescriptionLines[0].StartsWith("Przelew z rachunku:"));
        Assert.Contains(rows, r => r.DescriptionLines[0].StartsWith("Przelew na rachunek:"));
        Assert.Contains(rows, r => r.Balance >= 1000m);
        Assert.Equal(rows, VeloBankSampleData.MultiPageRows.Take(VeloBankSampleData.OnePageRowCount));
    }

    [Fact]
    public void SampleData_MultiPageRows_HaveFortyRowsAndAFiveLineDescription()
    {
        // Arrange
        var rows = VeloBankSampleData.MultiPageRows;

        // Assert
        Assert.Equal(VeloBankSampleData.MultiPageRowCount, rows.Count);
        Assert.Contains(rows, r => r.DescriptionLines.Count == 5);
    }

    [Fact]
    public void SampleData_BookedPlnRows_FormOneExactBalanceChain()
    {
        // Arrange
        var booked = VeloBankSampleData.MultiPageRows.Where(r => r.BookingDate is not null && r.Currency == "PLN").ToList();

        // Assert
        for (var i = 0; i < booked.Count - 1; i++)
            Assert.Equal(booked[i + 1].Balance, booked[i].Balance - booked[i].Amount);
        Assert.All(booked, r => Assert.True(r.Balance > 0));
    }

    [Fact]
    public void SampleData_CardDescriptions_RepeatTheAmountColumn()
    {
        // Arrange
        var cardRows = VeloBankSampleData.MultiPageRows.Where(r => r.DescriptionLines[0].StartsWith("Operacja kartą")).ToList();

        // Assert
        Assert.NotEmpty(cardRows);
        Assert.All(cardRows, r =>
            Assert.Contains($"na kwotę {VeloBankPdfBuilder.FormatMoney(Math.Abs(r.Amount))} PLN", r.DescriptionLines[0]));
    }
}
