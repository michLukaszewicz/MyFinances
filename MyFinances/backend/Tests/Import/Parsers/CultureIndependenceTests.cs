using System.Globalization;
using System.Text;
using MyFinances.Api.Import;
using MyFinances.Api.Tests.Support;
using Xunit;

namespace MyFinances.Api.Tests.Import.Parsers;

// Each parser reads one key fixture under every culture of CultureMatrix and must return the same
// authored dates and amounts, including a decimal comma and a thousands-separated amount.
public class CultureIndependenceTests
{
    private readonly MBankCsvParser _mBankCsv = new();
    private readonly ErsteCsvParser _ersteCsv = new();
    private readonly MBankPdfParser _mBankPdf = new();
    private readonly VeloBankPdfParser _veloBankPdf = new();
    private readonly ErstePdfParser _ersteBankPdf = new();

    public CultureIndependenceTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private static byte[] ReadFixture(string name) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public void PolishCulture_UsesADecimalComma_OnThisRuntime()
    {
        // Act
        var separator = CultureInfo.GetCultureInfo("pl-PL").NumberFormat.NumberDecimalSeparator;

        // Assert
        // Guards the pl-PL assumption: an ICU-less runtime falls back to invariant data here.
        Assert.Equal(",", separator);
    }

    [Fact]
    public void MBankCsv_ReadsTheSameRows_UnderEveryCulture()
    {
        // Arrange
        var bytes = MBankCsvBuilder.Build(
        [
            new MBankCsvRow(new DateOnly(2026, 8, 1), "THOUSANDS", 1234.56m),
            new MBankCsvRow(new DateOnly(2026, 12, 31), "COMMA", -7.50m),
        ]);
        var cp1250 = Encoding.GetEncoding(1250);
        var text = cp1250.GetString(bytes).Replace(";1234,56;", ";1 234,56;");
        var withThousands = cp1250.GetBytes(text);

        // Act
        var results = CultureMatrix.Run(() => _mBankCsv.Parse(new MemoryStream(withThousands)));

        // Assert
        Assert.Equal(CultureMatrix.Names.Count, results.Count);
        Assert.All(results, r =>
        {
            Assert.Equal(0, r.Result.SkippedErrorCount);
            Assert.Equal(
            [
                new NormalizedTransaction(new DateOnly(2026, 8, 1), "THOUSANDS", 1234.56m),
                new NormalizedTransaction(new DateOnly(2026, 12, 31), "COMMA", -7.50m),
            ],
            r.Result.Transactions);
        });
    }

    [Fact]
    public void ErsteCsv_ReadsTheSameRows_UnderEveryCulture()
    {
        // Arrange
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "erste-sample-redacted.csv"), new UTF8Encoding(false))
            .Replace(";-69,98;", ";-1 069,98;");
        var bytes = Encoding.UTF8.GetBytes(text);

        // Act
        var results = CultureMatrix.Run(() => _ersteCsv.Parse(new MemoryStream(bytes)));

        // Assert
        Assert.All(results, r =>
        {
            Assert.Equal(0, r.Result.SkippedErrorCount);
            Assert.Contains(r.Result.Transactions, t => t.Date == new DateOnly(2026, 9, 26) && t.Amount == -1069.98m);
            Assert.Contains(r.Result.Transactions, t => t.Date == new DateOnly(2026, 9, 21) && t.Amount == 500.00m);
        });
    }

    [Fact]
    public void MBankPdf_ReadsTheSameRows_UnderEveryCulture()
    {
        // Arrange
        var pdf = ReadFixture("mbank-pdf-sample-synthetic.pdf");

        // Act
        var results = CultureMatrix.Run(() => _mBankPdf.Parse(new MemoryStream(pdf)));

        // Assert
        Assert.All(results, r =>
        {
            Assert.Contains(r.Result.Transactions, t => t.Date == new DateOnly(2026, 9, 17) && t.Amount == 3200.00m);
            Assert.Contains(r.Result.Transactions, t => t.Date == new DateOnly(2026, 9, 17) && t.Amount == -1200.00m);
            Assert.Contains(r.Result.Transactions, t => t.Date == new DateOnly(2026, 9, 14) && t.Amount == -142.35m);
        });
    }

    [Fact]
    public void VeloBankPdf_ReadsTheSameRows_UnderEveryCulture()
    {
        // Arrange
        var pdf = ReadFixture("velobank-sample-synthetic.pdf");

        // Act
        var results = CultureMatrix.Run(() => _veloBankPdf.Parse(new MemoryStream(pdf)));

        // Assert
        Assert.All(results, r =>
        {
            Assert.Contains(r.Result.Transactions, t => t.Date == new DateOnly(2026, 9, 23) && t.Amount == 1200.00m);
            Assert.Contains(r.Result.Transactions, t => t.Amount == -89.00m);
        });
    }

    [Fact]
    public void ErstePdf_ReadsTheSameRows_UnderEveryCulture()
    {
        // Arrange
        var pdf = ReadFixture("erste-pdf-sample-synthetic.pdf");

        // Act
        var results = CultureMatrix.Run(() => _ersteBankPdf.Parse(new MemoryStream(pdf)));

        // Assert
        Assert.All(results, r =>
        {
            Assert.Contains(r.Result.Transactions, t => t.Date == new DateOnly(2026, 9, 29) && t.Amount == 1200.00m);
            Assert.Contains(r.Result.Transactions, t => t.Date == new DateOnly(2026, 9, 30) && t.Amount == 29.99m);
            Assert.Contains(r.Result.Transactions, t => t.Date == new DateOnly(2026, 9, 30) && t.Amount == -142.35m);
        });
    }
}
