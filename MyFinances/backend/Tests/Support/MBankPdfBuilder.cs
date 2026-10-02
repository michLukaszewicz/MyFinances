using System.Globalization;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Writer;

namespace MyFinances.Api.Tests.Support;

// One row of the mBank "Elektroniczne zestawienie operacji" table, as it is printed: booking date,
// operation date, the description lines (the first one is the operation type), amount and the
// balance after the operation. Amount and balance carry no currency (the statement prints it once).
// The *Text overrides replace the whole printed cell text, so a test can produce an unreadable cell.
public sealed record MBankPdfRow(
    DateOnly BookingDate,
    DateOnly OperationDate,
    IReadOnlyList<string> DescriptionLines,
    decimal Amount,
    decimal Balance)
{
    public string? BookingDateText { get; init; }
    public string? AmountText { get; init; }
    public string? BalanceText { get; init; }
}

// The account block on page 1: invented holder and account data. Currency is the "Waluta" field.
public sealed record MBankPdfHeader(
    string HolderName,
    string AccountNumber,
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    string Currency);

// Turnover summary figures as printed: credits and debits are positive, the total is their difference.
public sealed record MBankPdfSummary(int CreditCount, decimal CreditSum, int DebitCount, decimal DebitSum);

// Page breaks and tamper hooks. The defaults mirror the real two-page export: 13 rows on page 1
// (the letterhead, account block and summary take space) and more on every later page.
public sealed record MBankPdfLayout
{
    public int FirstPageRows { get; init; } = 13;
    public int RowsPerPage { get; init; } = 24;

    // Printed instead of the figures computed from the rows.
    public MBankPdfSummary? SummaryOverride { get; init; }
    public decimal? ClosingBalanceOverride { get; init; }

    // Leave the figure out entirely, to test how a parser treats a statement that lacks it.
    public bool OmitOpeningBalance { get; init; }
    public bool OmitClosingBalance { get; init; }
    public bool OmitSummary { get; init; }
}

// Writes an mBank-style statement PDF from invented rows, reproducing the geometry measured on the
// real export (page 595.2 x 828, Arial 8 pt, summary table, a header row repeated on every page, a
// date line per row with amount and balance on it and the description continuing below, closing
// balance under the last row). The grid is drawn as stroked lines, as in the real file. The PDF
// needs a TrueType font with Polish diacritics: Fixtures/fonts/NotoSans-Regular.ttf (SIL OFL).
public static class MBankPdfBuilder
{
    public const double PageWidth = 595.238;
    public const double PageHeight = 828;

    // Column edges of the operations table (booking date | operation date | description | amount | balance).
    public static readonly IReadOnlyList<double> ColumnEdges = [40.0, 96.7, 148.2, 452.2, 503.7, 555.2];

    // Column edges of the three-column turnover summary table.
    public static readonly IReadOnlyList<double> SummaryEdges = [40.0, 246.1, 348.6, 452.2];

    public const double TextSize = 8.0;
    public const double LineStep = 9.6;
    public const double HeaderHeight = 26.9;

    // Lowest allowed bottom edge of the table on a page that is not the last one.
    public const double MinTableBottom = 69.0;

    private const double LeftMargin = 40.0;
    private const double RightMargin = 555.2;
    private const double FirstPageHeaderTop = 407.3;
    private const double NextPageHeaderTop = 753.7;
    private const double SummaryTop = 535.5;
    private const double SummaryRowHeight = 16.3;
    private const double OpeningBalanceHeight = 22.3;
    private const double ClosingBalanceHeight = 22.2;
    private const double RowBaseHeight = 6.65;
    private const double FirstLineBaseline = 11.4;
    private const double GridLineWidth = 0.5;

    // Lowest allowed baseline of the boilerplate paragraph under the closing balance.
    private const double MinBoilerplateBaseline = 45.0;

    private static readonly (byte R, byte G, byte B) Black = (0, 0, 0);
    private static readonly (byte R, byte G, byte B) GridGray = (153, 153, 153);

    private static readonly Lazy<byte[]> FontBytes = new(
        () => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "fonts", "NotoSans-Regular.ttf")));

    public static byte[] Build(
        MBankPdfHeader header, decimal openingBalance, IReadOnlyList<MBankPdfRow> rows, MBankPdfLayout? layout = null)
    {
        layout ??= new MBankPdfLayout();
        if (layout.FirstPageRows < 1 || layout.RowsPerPage < 1)
            throw new ArgumentException("Rows per page must be positive.", nameof(layout));

        var pages = Paginate(rows, layout);
        var summary = layout.SummaryOverride ?? ComputeSummary(rows);
        var closingBalance = layout.ClosingBalanceOverride ?? (rows.Count > 0 ? rows[^1].Balance : openingBalance);

        using var builder = new PdfDocumentBuilder();
        // Fixed metadata keeps regenerated PDFs stable.
        builder.DocumentInformation.Title = "Elektroniczne zestawienie operacji (dane testowe)";
        builder.DocumentInformation.Creator = "MyFinances.Api.Tests";
        builder.DocumentInformation.Producer = "MyFinances.Api.Tests";
        builder.DocumentInformation.CreationDate = "D:20261001000000Z";
        builder.DocumentInformation.ModifiedDate = "D:20261001000000Z";
        var font = builder.AddTrueTypeFont(FontBytes.Value);

        for (var pageIndex = 0; pageIndex < pages.Count; pageIndex++)
        {
            var isLast = pageIndex == pages.Count - 1;
            var painter = new Painter(builder.AddPage(PageWidth, PageHeight), font);
            double y;
            if (pageIndex == 0)
            {
                DrawLetterhead(painter);
                DrawAccountBlock(painter, header);
                if (!layout.OmitSummary)
                    DrawSummary(painter, summary);
                painter.TextCentered("Operacje", PageWidth / 2, 446.3, 10);
                y = DrawOpeningBalance(painter, openingBalance, layout.OmitOpeningBalance);
                y = DrawHeaderBlock(painter, y);
            }
            else
            {
                y = DrawHeaderBlock(painter, NextPageHeaderTop);
            }

            foreach (var row in pages[pageIndex])
                y = DrawRow(painter, y, row);

            if (y < MinTableBottom)
                throw new InvalidOperationException($"The rows on page {pageIndex + 1} run into the footer; use more pages.");

            if (isLast)
            {
                y = DrawClosingBalance(painter, y, closingBalance, layout.OmitClosingBalance);
                DrawBoilerplate(painter, y);
            }

            painter.Text($"Strona : {pageIndex + 1} / {pages.Count}", 518.4, 31.4, TextSize);
        }

        var pdf = builder.Build();
        VeloBankPdfBuilder.PinTrailerId(pdf);
        return pdf;
    }

    // "-1 014,84": comma decimals and a real space as the thousands separator, as in the real files.
    public static string FormatMoney(decimal value) => VeloBankPdfBuilder.FormatMoney(value);

    public static string FormatDate(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static MBankPdfSummary ComputeSummary(IReadOnlyList<MBankPdfRow> rows) => new(
        rows.Count(r => r.Amount > 0),
        rows.Where(r => r.Amount > 0).Sum(r => r.Amount),
        rows.Count(r => r.Amount < 0),
        -rows.Where(r => r.Amount < 0).Sum(r => r.Amount));

    // Height of a row band: two lines fit the measured 25.85 pt pitch, every further line adds one line step.
    public static double RowHeight(int descriptionLines) => RowBaseHeight + LineStep * Math.Max(2, descriptionLines);

    private static List<List<MBankPdfRow>> Paginate(IReadOnlyList<MBankPdfRow> rows, MBankPdfLayout layout)
    {
        var pages = new List<List<MBankPdfRow>> { rows.Take(layout.FirstPageRows).ToList() };
        for (var next = layout.FirstPageRows; next < rows.Count; next += layout.RowsPerPage)
            pages.Add(rows.Skip(next).Take(layout.RowsPerPage).ToList());
        return pages;
    }

    // Invented letterhead with the shape of the real one: six left-aligned lines and a short right-aligned label.
    private static void DrawLetterhead(Painter p)
    {
        string[] lines =
        [
            "mBank S.A. Oddział Testowy",
            "Ulica Testowa 1/2",
            "00-000 Testowo",
            "www.przyklad.test",
            "Infolinia: 000 000 000",
            "+00 (00) 0 000 000",
        ];
        for (var i = 0; i < lines.Length; i++)
            p.Text(lines[i], LeftMargin, 746.1 - i * LineStep, TextSize);
        p.TextRight("DOKUMENT TESTOWY", RightMargin, 688.5, TextSize);
    }

    private static void DrawAccountBlock(Painter p, MBankPdfHeader header)
    {
        const double valueX = 210.0;
        p.Text("Opis rachunku", LeftMargin, 639.3, TextSize);
        (string Label, string Value, double Baseline)[] lines =
        [
            ("Właściciel rachunku", header.HolderName, 628.9),
            ("Waluta", header.Currency, 619.3),
            ("Numer rachunku", header.AccountNumber, 609.7),
            ("Data wygenerowania", FormatDate(header.PeriodTo.AddDays(1)), 595.1),
            ("Oprocentowanie rachunku", "0,00%", 585.5),
            ("Limit debetowy", "0,00 PLN", 575.9),
            ("Oprocentowanie debetu", "0,00%", 566.3),
        ];
        foreach (var (label, value, baseline) in lines)
        {
            p.Text(label, LeftMargin, baseline, TextSize);
            p.Text(value, valueX, baseline, TextSize);
        }

        p.TextCentered(
            $"Elektroniczne zestawienie operacji za okres od {FormatDate(header.PeriodFrom)} do {FormatDate(header.PeriodTo)}",
            PageWidth / 2, 662.8, 10);
    }

    private static void DrawSummary(Painter p, MBankPdfSummary summary)
    {
        var total = summary.CreditSum - summary.DebitSum;
        (string C0, string C1, string C2)[] cells =
        [
            ("Podsumowanie obrotów na rachunku", "Liczba operacji", "Wartość operacji"),
            ("Uznania", summary.CreditCount.ToString(CultureInfo.InvariantCulture), FormatMoney(summary.CreditSum)),
            ("Obciążenia", summary.DebitCount.ToString(CultureInfo.InvariantCulture), FormatMoney(summary.DebitSum)),
            ("Łącznie", (summary.CreditCount + summary.DebitCount).ToString(CultureInfo.InvariantCulture), FormatMoney(total)),
        ];

        for (var i = 0; i < cells.Length; i++)
        {
            var top = SummaryTop - i * SummaryRowHeight;
            var bottom = top - SummaryRowHeight;
            p.CellGrid(SummaryEdges, top, bottom);
            var baseline = bottom + 5.4;
            p.Text(cells[i].C0, SummaryEdges[0] + 3.8, baseline, TextSize);
            if (i == 0)
            {
                p.Text(cells[i].C1, SummaryEdges[1] + 3.3, baseline, TextSize);
                p.Text(cells[i].C2, SummaryEdges[2] + 3.9, baseline, TextSize);
            }
            else
            {
                p.TextRight(cells[i].C1, SummaryEdges[2] - 2.8, baseline, TextSize);
                p.TextRight(cells[i].C2, SummaryEdges[3] - 3.9, baseline, TextSize);
            }
        }
    }

    // "Saldo początkowe: 3 100,00", right-aligned in a full-width cell above the table; returns the cell's bottom edge.
    private static double DrawOpeningBalance(Painter p, decimal opening, bool omit)
    {
        var top = FirstPageHeaderTop + OpeningBalanceHeight;
        p.CellGrid([LeftMargin, RightMargin], top, FirstPageHeaderTop);
        if (!omit)
            p.TextRight($"Saldo początkowe: {FormatMoney(opening)}", RightMargin - 1.0, 413.1, TextSize);
        return FirstPageHeaderTop;
    }

    // Draws the table header cells and their texts; returns the bottom edge of the block.
    private static double DrawHeaderBlock(Painter p, double top)
    {
        var bottom = top - HeaderHeight;
        p.CellGrid(ColumnEdges, top, bottom);

        const double upper = 15.5, lower = 5.9;
        p.Text("Data", ColumnEdges[0] + 3.8, bottom + upper, TextSize);
        p.Text("księgowania", ColumnEdges[0] + 3.8, bottom + lower, TextSize);
        p.Text("Data", ColumnEdges[1] + 3.3, bottom + upper, TextSize);
        p.Text("operacji", ColumnEdges[1] + 3.3, bottom + lower, TextSize);
        p.Text("Opis operacji", ColumnEdges[2] + 3.3, bottom + upper, TextSize);
        p.Text("Kwota", ColumnEdges[3] + 3.3, bottom + upper, TextSize);
        p.Text("Saldo po", ColumnEdges[4] + 3.3, bottom + upper, TextSize);
        p.Text("operacji", ColumnEdges[4] + 3.3, bottom + lower, TextSize);
        return bottom;
    }

    // Draws one row band; the date line carries both dates, the first description line, amount and
    // balance, further description lines follow below. Returns the next band's top.
    private static double DrawRow(Painter p, double top, MBankPdfRow row)
    {
        var height = RowHeight(row.DescriptionLines.Count);
        var bottom = top - height;
        p.CellGrid(ColumnEdges, top, bottom);

        var dateLine = top - FirstLineBaseline;
        p.Text(row.BookingDateText ?? FormatDate(row.BookingDate), 48.1, dateLine, TextSize);
        p.Text(FormatDate(row.OperationDate), 102.0, dateLine, TextSize);
        for (var i = 0; i < row.DescriptionLines.Count; i++)
            p.Text(row.DescriptionLines[i], 151.5, dateLine - i * LineStep, TextSize);
        p.TextRight(row.AmountText ?? FormatMoney(row.Amount), 500.4, dateLine, TextSize);
        p.TextRight(row.BalanceText ?? FormatMoney(row.Balance), 551.4, dateLine, TextSize);
        return bottom;
    }

    // "Saldo końcowe: 3 473,70" in a full-width cell under the last row; returns the cell's bottom edge.
    private static double DrawClosingBalance(Painter p, double top, decimal closing, bool omit)
    {
        var bottom = top - ClosingBalanceHeight;
        p.CellGrid([LeftMargin, RightMargin], top, bottom);
        if (!omit)
            p.TextRight($"Saldo końcowe: {FormatMoney(closing)}", RightMargin - 1.0, bottom + 5.8, TextSize);
        return bottom;
    }

    // Invented boilerplate with the shape of the real one: four lines of small print under the closing balance.
    private static void DrawBoilerplate(Painter p, double tableBottom)
    {
        string[] lines =
        [
            "Niniejsze zestawienie wygenerowano automatycznie na potrzeby testów aplikacji i nie jest dokumentem bankowym.",
            "Wszystkie dane są zmyślone, a podobieństwo do rzeczywistych osób, rachunków lub transakcji jest przypadkowe.",
            "Dane rejestrowe wymyślone na potrzeby testów: wpis nr 0000000000, NIP 0000000000, REGON 000000000.",
            "Koniec dokumentu testowego.",
        ];
        var baseline = tableBottom - 21.8;
        if (baseline - (lines.Length - 1) * LineStep < MinBoilerplateBaseline)
            throw new InvalidOperationException("The closing balance and the boilerplate run into the footer; use more pages.");
        for (var i = 0; i < lines.Length; i++)
            p.Text(lines[i], LeftMargin, baseline - i * LineStep, TextSize);
    }

    private sealed class Painter(PdfPageBuilder page, PdfDocumentBuilder.AddedFont font)
    {
        // The grid of one table row: for every column a top and a bottom line, plus the vertical column
        // edges, all stroked lines (never filled rectangles).
        public void CellGrid(IReadOnlyList<double> edges, double top, double bottom)
        {
            page.SetStrokeColor(GridGray.R, GridGray.G, GridGray.B);
            for (var c = 0; c < edges.Count - 1; c++)
            {
                page.DrawLine(new PdfPoint(edges[c], top), new PdfPoint(edges[c + 1], top), GridLineWidth);
                page.DrawLine(new PdfPoint(edges[c], bottom), new PdfPoint(edges[c + 1], bottom), GridLineWidth);
            }
            foreach (var x in edges)
                page.DrawLine(new PdfPoint(x, bottom), new PdfPoint(x, top), GridLineWidth);
        }

        public void Text(string text, double x, double baseline, double size)
        {
            page.SetTextAndFillColor(Black.R, Black.G, Black.B);
            page.AddText(text, size, new PdfPoint(x, baseline), font);
        }

        public void TextRight(string text, double right, double baseline, double size) =>
            Text(text, right - Width(text, size), baseline, size);

        public void TextCentered(string text, double center, double baseline, double size) =>
            Text(text, center - Width(text, size) / 2, baseline, size);

        private double Width(string text, double size)
        {
            var letters = page.MeasureText(text, size, new PdfPoint(0, 0), font);
            return letters[^1].EndBaseLine.X - letters[0].StartBaseLine.X;
        }
    }
}
