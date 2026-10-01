using System.Text;
using MyFinances.Api.Import;
using Xunit;

namespace MyFinances.Api.Tests;

public class StatementFormatSnifferTests
{
    private static string FixturePath(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Fact]
    public void Detect_ReturnsPdf_ForPdfMagicBytes()
    {
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.7\n%binary garbage follows"));

        Assert.Equal(StatementFormat.Pdf, StatementFormatSniffer.Detect(stream));
    }

    [Fact]
    public void Detect_ReturnsPdf_ForExactlyTheFiveMagicBytes()
    {
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes("%PDF-"));

        Assert.Equal(StatementFormat.Pdf, StatementFormatSniffer.Detect(stream));
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
    public void Detect_ReturnsCsv_ForEveryExistingCsvFixture(string fileName)
    {
        using var stream = File.OpenRead(FixturePath(fileName));

        Assert.Equal(StatementFormat.Csv, StatementFormatSniffer.Detect(stream));
    }

    [Fact]
    public void Detect_ReturnsCsv_ForEmptyStream()
    {
        using var stream = new MemoryStream();

        Assert.Equal(StatementFormat.Csv, StatementFormatSniffer.Detect(stream));
    }

    [Fact]
    public void Detect_ReturnsCsv_ForStreamShorterThanTheMagic()
    {
        using var stream = new MemoryStream("%PD"u8.ToArray());

        Assert.Equal(StatementFormat.Csv, StatementFormatSniffer.Detect(stream));
    }

    [Fact]
    public void Detect_ReturnsCsv_ForPartialMagicFollowedByOtherBytes()
    {
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes("%PDF_ not quite"));

        Assert.Equal(StatementFormat.Csv, StatementFormatSniffer.Detect(stream));
    }

    [Fact]
    public void Detect_RestoresStreamPosition_ForSeekableStream()
    {
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.7 body"));

        StatementFormatSniffer.Detect(stream);

        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void Detect_RestoresNonZeroStartPosition_ForSeekableStream()
    {
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes("xx%PDF-1.7 body"));
        stream.Position = 2;

        var format = StatementFormatSniffer.Detect(stream);

        Assert.Equal(StatementFormat.Pdf, format);
        Assert.Equal(2, stream.Position);
    }

    [Fact]
    public void CsvParsers_DeclareCsvFormat()
    {
        Assert.Equal(StatementFormat.Csv, new MBankCsvParser().Format);
        Assert.Equal(StatementFormat.Csv, new ErsteCsvParser().Format);
    }
}
