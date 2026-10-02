using System.Globalization;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Writer;

namespace MyFinances.Api.Tests.Support;

// One row of the VeloBank "Historia rachunku" table, as it is printed.
// BookingDate == null prints "-" (pending); Balance == null prints "-" (pending rows have no balance).
// The *Text overrides replace the whole printed cell text, so a test can produce an unreadable cell.
public sealed record VeloBankPdfRow(
    DateOnly TransactionDate,
    DateOnly? BookingDate,
    IReadOnlyList<string> DescriptionLines,
    decimal Amount,
    string Currency,
    decimal? Balance)
{
    public string? TransactionDateText { get; init; }
    public string? AmountText { get; init; }
    public string? BalanceText { get; init; }
}

// The block above the table on page 1: invented holder and account data.
public sealed record VeloBankPdfHeader(string AccountNumber, string HolderName, string AddressLine, string PeriodLabel);

// Page breaks and layout switches. The defaults mirror the real four-page export:
// 15 rows on page 1 (the title block takes space), 19 on every later page.
public sealed record VeloBankPdfLayout
{
    public int FirstPageRows { get; init; } = 15;
    public int RowsPerPage { get; init; } = 19;

    // Prints a repeated header block (with the 0.58 pt shifted column edges seen in the real
    // one-page sample) on page 1 before the row with this zero-based index.
    public int? MidPageHeaderAfterRows { get; init; }

    // The real files put date, amount and balance at a fixed offset from the top of the row
    // (a hair below the first description line), not at the middle of a tall row.
    // true centres them vertically instead, to stress parsers that group words by baseline.
    public bool CenterValueCells { get; init; }

    // Leaves the header block off this (1-based) page while keeping its rows where they were, to
    // test how a parser treats a page whose table it cannot find.
    public int? OmitHeaderOnPage { get; init; }
}

// Writes a VeloBank-style "Historia rachunku" PDF from invented rows, reproducing the geometry
// measured on the real exports (A4, five columns, 28 pt header cells, thin filled row separators).
// The PDF needs a TrueType font with Polish diacritics: Fixtures/fonts/NotoSans-Regular.ttf (SIL OFL).
public static class VeloBankPdfBuilder
{
    public const double PageWidth = 595;
    public const double PageHeight = 842;

    // Column edges (transaction date | booking date | description | amount | balance after).
    public static readonly IReadOnlyList<double> ColumnEdges = [26.24, 89.21, 153.94, 440.23, 504.37, 567.93];

    // Edges of the header block repeated mid-page in the real one-page sample (and of the rows below it).
    public static readonly IReadOnlyList<double> MidPageColumnEdges = [26.24, 89.80, 154.52, 439.65, 503.79, 567.93];

    public const double HeaderHeight = 27.99;
    public const double SeparatorHeight = 1.17;
    public const double TextSize = 5.25;
    public const double LineStep = 5.83;

    // Lowest allowed bottom edge of the table; below it the footer lines start.
    public const double MinTableBottom = 100;

    private const double FirstPageHeaderTop = 616.22;
    private const double NextPageHeaderTop = 734.00;
    private const double MidPageHeaderGap = 10.5;
    private const double DividerWidth = 0.58;
    private const double CellPadding = 8.75;
    private const double BalanceRightPadding = 8.5;
    private const double HeaderRightPadding = 9.2;
    private const double FirstDescriptionBaseline = 10.5;
    private const double ValueBaseline = 11.66;
    private const double CenteredValueBaseline = 1.9;
    private const double MinRowLines = 3.5;

    private static readonly (byte R, byte G, byte B) Black = (0, 0, 0);
    private static readonly (byte R, byte G, byte B) HeaderGray = (189, 190, 193);
    private static readonly (byte R, byte G, byte B) LightGray = (229, 229, 229);
    private static readonly (byte R, byte G, byte B) White = (255, 255, 255);

    private static readonly Lazy<byte[]> FontBytes = new(
        () => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "fonts", "NotoSans-Regular.ttf")));

    public static byte[] Build(VeloBankPdfHeader header, IReadOnlyList<VeloBankPdfRow> rows, VeloBankPdfLayout? layout = null)
    {
        layout ??= new VeloBankPdfLayout();
        if (layout.FirstPageRows < 1 || layout.RowsPerPage < 1)
            throw new ArgumentException("Rows per page must be positive.", nameof(layout));

        var pages = Paginate(rows, layout);
        if (layout.MidPageHeaderAfterRows is { } midIndex && (midIndex < 1 || midIndex >= pages[0].Count))
            throw new ArgumentException("MidPageHeaderAfterRows must fall inside the rows of page 1.", nameof(layout));

        using var builder = new PdfDocumentBuilder();
        // Fixed metadata keeps regenerated PDFs stable.
        builder.DocumentInformation.Title = "Historia rachunku (dane testowe)";
        builder.DocumentInformation.Creator = "MyFinances.Api.Tests";
        builder.DocumentInformation.Producer = "MyFinances.Api.Tests";
        builder.DocumentInformation.CreationDate = "D:20261001000000Z";
        builder.DocumentInformation.ModifiedDate = "D:20261001000000Z";
        var font = builder.AddTrueTypeFont(FontBytes.Value);

        for (var pageIndex = 0; pageIndex < pages.Count; pageIndex++)
        {
            var painter = new Painter(builder.AddPage(PageWidth, PageHeight), font);
            var edges = ColumnEdges;
            double y;
            if (pageIndex == 0)
            {
                DrawTitleBlock(painter, header);
                y = FirstPageHeaderTop;
            }
            else
            {
                y = NextPageHeaderTop;
            }
            y = layout.OmitHeaderOnPage == pageIndex + 1 ? y - HeaderHeight : DrawHeaderBlock(painter, edges, y);

            var pageRows = pages[pageIndex];
            for (var i = 0; i < pageRows.Count; i++)
            {
                if (pageIndex == 0 && layout.MidPageHeaderAfterRows == i)
                {
                    edges = MidPageColumnEdges;
                    y = DrawHeaderBlock(painter, edges, y - MidPageHeaderGap);
                }
                y = DrawRow(painter, edges, y, pageRows[i], layout);
            }

            if (y < MinTableBottom)
                throw new InvalidOperationException($"The rows on page {pageIndex + 1} run into the footer; use more pages.");
            DrawFooter(painter);
        }

        var pdf = builder.Build();
        PinTrailerId(pdf);
        return pdf;
    }

    // PdfPig writes a random /ID into the trailer; overwriting it in place (same length, so no
    // offset moves) makes two builds of the same input byte-identical.
    private static void PinTrailerId(byte[] pdf)
    {
        ReadOnlySpan<byte> marker = "/ID [ <"u8;
        var start = pdf.AsSpan().LastIndexOf(marker);
        if (start < 0)
            return;

        const int idLength = 32;
        var first = start + marker.Length;
        var second = first + idLength + 2;
        if (second + idLength >= pdf.Length || pdf[first + idLength] != (byte)'>' || pdf[first + idLength + 1] != (byte)'<')
            return;

        pdf.AsSpan(first, idLength).Fill((byte)'0');
        pdf.AsSpan(second, idLength).Fill((byte)'0');
    }

    // "-1 014,84": comma decimals and a real space as the thousands separator, as in the real files.
    public static string FormatMoney(decimal value)
    {
        var digits = Math.Abs(value).ToString("#,##0.00", CultureInfo.InvariantCulture)
            .Replace(',', ' ')
            .Replace('.', ',');
        return value < 0 ? "-" + digits : digits;
    }

    public static string FormatDate(DateOnly date) => date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

    private static List<List<VeloBankPdfRow>> Paginate(IReadOnlyList<VeloBankPdfRow> rows, VeloBankPdfLayout layout)
    {
        var pages = new List<List<VeloBankPdfRow>> { rows.Take(layout.FirstPageRows).ToList() };
        for (var next = layout.FirstPageRows; next < rows.Count; next += layout.RowsPerPage)
            pages.Add(rows.Skip(next).Take(layout.RowsPerPage).ToList());
        return pages;
    }

    private static void DrawTitleBlock(Painter p, VeloBankPdfHeader header)
    {
        const double left = 26.24;
        const double width = 541.69;
        p.Rect(left, 680.94, width, 2.91, HeaderGray);
        p.Rect(left, 678.02, width, 2.92, HeaderGray);
        p.Rect(left, 663.45, width, 0.58, LightGray);
        p.Rect(left, 637.79, width, 0.58, LightGray);

        p.Text("Historia rachunku", 33.82, 700.18, 8.75);
        p.Text("NUMER RACHUNKU:", 34.40, 669.28, TextSize);
        p.Text(header.AccountNumber, 212.24, 669.28, 5.83);
        p.Text("POSIADACZ RACHUNKU:", 34.40, 654.70, TextSize);
        p.Text(header.HolderName, 212.24, 654.70, 5.83);
        p.Text(header.AddressLine, 212.24, 643.62, 5.83);
        p.Text(header.PeriodLabel, left, 629.04, 5.83);
    }

    // Draws the grey 28 pt header cells and their texts; returns the bottom edge of the block.
    private static double DrawHeaderBlock(Painter p, IReadOnlyList<double> edges, double top)
    {
        var bottom = top - HeaderHeight;
        for (var c = 0; c < 5; c++)
        {
            p.Rect(edges[c], bottom, edges[c + 1] - edges[c], HeaderHeight, HeaderGray);
            p.Rect(edges[c + 1] - DividerWidth, bottom, DividerWidth, HeaderHeight, White);
        }

        const double upper = 14.58, middle = 12.25, lower = 9.33;
        p.Text("DATA", edges[0] + CellPadding, bottom + upper, TextSize);
        p.Text("TRANSAKCJI", edges[0] + CellPadding, bottom + lower, TextSize);
        p.Text("DATA", edges[1] + CellPadding, bottom + upper, TextSize);
        p.Text("KSIĘGOWANIA", edges[1] + CellPadding, bottom + lower, TextSize);
        p.Text("OPIS TRANSAKCJI", edges[2] + CellPadding, bottom + middle, TextSize);
        p.TextRight("KWOTA", edges[4] - HeaderRightPadding, bottom + upper, TextSize);
        p.TextRight("TRANSAKCJI", edges[4] - HeaderRightPadding, bottom + lower, TextSize);
        p.TextRight("SALDO PO", edges[5] - HeaderRightPadding, bottom + upper, TextSize);
        p.TextRight("TRANSAKCJI", edges[5] - HeaderRightPadding, bottom + lower, TextSize);
        return bottom;
    }

    // Draws one row band plus its separator (one thin piece per column); returns the next band's top.
    private static double DrawRow(Painter p, IReadOnlyList<double> edges, double top, VeloBankPdfRow row, VeloBankPdfLayout layout)
    {
        var lines = row.DescriptionLines.Count;
        var bandHeight = Math.Max(MinRowLines, lines + 2) * LineStep;
        var valueBaseline = layout.CenterValueCells
            ? top - bandHeight / 2 - CenteredValueBaseline
            : top - ValueBaseline;

        for (var i = 0; i < lines; i++)
            p.Text(row.DescriptionLines[i], ColumnEdges[2] + CellPadding, top - FirstDescriptionBaseline - i * LineStep, TextSize);

        p.Text(row.TransactionDateText ?? FormatDate(row.TransactionDate), ColumnEdges[0] + CellPadding, valueBaseline, TextSize);
        p.Text(row.BookingDate is { } booked ? FormatDate(booked) : "-", ColumnEdges[1] + CellPadding, valueBaseline, TextSize);
        p.TextRight(row.AmountText ?? $"{FormatMoney(row.Amount)} {row.Currency}", ColumnEdges[4] - CellPadding, valueBaseline, TextSize);
        p.TextRight(row.BalanceText ?? (row.Balance is { } balance ? $"{FormatMoney(balance)} PLN" : "-"),
            ColumnEdges[5] - BalanceRightPadding, valueBaseline, TextSize);

        var separatorBottom = top - bandHeight - SeparatorHeight;
        for (var c = 0; c < 5; c++)
            p.Rect(edges[c], separatorBottom, edges[c + 1] - edges[c], SeparatorHeight, HeaderGray);
        return separatorBottom;
    }

    // Invented boilerplate with the shape of the real footer: two larger lines, then three lines of small print.
    private static void DrawFooter(Painter p)
    {
        const double left = 51.01;
        p.Text("Niniejsze zestawienie wygenerowano automatycznie na potrzeby testów aplikacji i nie jest dokumentem bankowym.", left, 69.60, 7.2);
        p.Text("(Wszystkie dane są zmyślone. Podobieństwo do rzeczywistych osób, rachunków lub transakcji jest przypadkowe.)", left, 62.40, 7.2);
        p.Text("VeloBank S.A. - stopka testowa odwzorowująca układ oryginalnego dokumentu: dwie linie informacji ogólnej i trzy linie drobnego druku.", left, 36.60, 4.8);
        p.Text("Dane rejestrowe wymyślone na potrzeby testów: wpis nr 0000000000, NIP 0000000000, REGON 000000000, kapitał zakładowy 000 000 000,00 zł.", left, 31.80, 4.8);
        p.Text("Koniec dokumentu.", left, 27.00, 4.8);
    }

    private sealed class Painter(PdfPageBuilder page, PdfDocumentBuilder.AddedFont font)
    {
        public void Rect(double x, double y, double width, double height, (byte R, byte G, byte B) color)
        {
            page.SetStrokeColor(color.R, color.G, color.B);
            page.SetTextAndFillColor(color.R, color.G, color.B);
            page.DrawRectangle(new PdfPoint(x, y), width, height, 0, true);
        }

        public void Text(string text, double x, double baseline, double size)
        {
            page.SetTextAndFillColor(Black.R, Black.G, Black.B);
            page.AddText(text, size, new PdfPoint(x, baseline), font);
        }

        public void TextRight(string text, double right, double baseline, double size)
        {
            var letters = page.MeasureText(text, size, new PdfPoint(0, 0), font);
            var width = letters[^1].EndBaseLine.X - letters[0].StartBaseLine.X;
            Text(text, right - width, baseline, size);
        }
    }
}
