namespace MyFinances.Api.Tests.Support;

// One transaction authored once and printed in both mBank export formats. The CSV carries only the
// title; the PDF carries an operation-type line followed by the title line, as the real exports do,
// so the parsed descriptions differ between the formats while date and amount agree.
public sealed record MBankPairedTransaction(DateOnly Date, decimal Amount, string Title)
{
    public const string OperationType = "TEST OPERATION";

    public IReadOnlyList<string> PdfDescriptionLines => [OperationType, Title];
}

// One independent list of invented transactions in a single month that yields both an mBank PDF and
// an mBank CSV describing the same account and period. Expected values in tests come from this
// list, never from a parser run.
public static class MBankPairedStatement
{
    public const decimal OpeningBalance = 1000.00m;

    public static MBankPdfHeader Header { get; } = new(
        HolderName: "JAN TESTOWY",
        AccountNumber: "00 0000 0000 0000 0000 0000 0000",
        PeriodFrom: new DateOnly(2026, 9, 1),
        PeriodTo: new DateOnly(2026, 9, 30),
        Currency: "PLN");

    // Oldest first, distinct dates and amounts.
    public static IReadOnlyList<MBankPairedTransaction> Transactions { get; } =
    [
        new(new DateOnly(2026, 9, 3), -40.00m, "Test description 1"),
        new(new DateOnly(2026, 9, 7), 150.00m, "Test description 2"),
        new(new DateOnly(2026, 9, 12), -75.25m, "Test description 3"),
        new(new DateOnly(2026, 9, 18), -10.50m, "Test description 4"),
        new(new DateOnly(2026, 9, 25), 300.00m, "Test description 5"),
    ];

    public static decimal AmountSum(IEnumerable<MBankPairedTransaction> transactions) => transactions.Sum(t => t.Amount);

    // All five rows fit on one page, drawn the way the real export draws them (one cell per row with
    // a small gap), so this also exercises the single-page path of the parser.
    public static MBankPdfLayout Layout { get; } = new() { FirstPageRows = 5, RowGap = 0.5 };

    public static byte[] BuildPdf()
    {
        var rows = MBankSampleData.WithRunningBalances(
            OpeningBalance,
            Transactions.Select(t => new MBankPdfRow(t.Date, t.Date, t.PdfDescriptionLines, t.Amount, 0m)).ToList());
        return MBankPdfBuilder.Build(Header, OpeningBalance, rows, Layout);
    }

    // The whole list by default; pass a subset to export only part of the period.
    public static byte[] BuildCsv(IEnumerable<MBankPairedTransaction>? subset = null) =>
        MBankCsvBuilder.Build((subset ?? Transactions).Select(t => new MBankCsvRow(t.Date, t.Title, t.Amount)).ToList());
}
