using System.Globalization;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Writer;

namespace MyFinances.Api.Tests.Support;

// One row of the Erste "Lista transakcji" table, as it is printed. Rows are passed in printed order
// (newest booking first); Balance is the printed balance after the row and is NOT derived from the
// order, because the real export prints same-day rows in an order that does not follow the balance chain.
// The *Text overrides replace the whole printed cell text, so a test can produce an unreadable cell.
public sealed record ErstePdfRow(
    DateOnly OperationDate,
    IReadOnlyList<string> OperationLines,
    decimal Amount,
    string Currency,
    decimal Balance)
{
    public string? DateText { get; init; }
    public string? AmountText { get; init; }
    public string? BalanceText { get; init; }
}

// The account line printed at the top of every page ("Konto: ...") and the "Dokument z dnia" date of the footer.
public sealed record ErstePdfHeader(string AccountNumber, DateOnly DocumentDate);

// Page breaks and layout switches. The real three-page export holds 20, 20 and 15 rows.
public sealed record ErstePdfLayout
{
    public int FirstPageRows { get; init; } = 20;
    public int RowsPerPage { get; init; } = 20;

    // Leaves the header cells ("Data operacji", ...) off this (1-based) page while keeping its rows
    // where they were, to test how a parser treats a page whose table it cannot find.
    public int? OmitHeaderOnPage { get; init; }

    // Leaves the "Konto:" line off every page.
    public bool OmitAccountLine { get; init; }

    // Leaves the "Lista transakcji" title off every page.
    public bool OmitTitle { get; init; }
}

// Writes an Erste-style "Lista transakcji" PDF from invented rows, reproducing the geometry measured
// on the real export (A4, four columns, 15 pt header cells, 30 pt row bands closed by thin filled
// separators, a "Data księgowania" label with no value under every date).
// The PDF needs a TrueType font with Polish diacritics: Fixtures/fonts/NotoSans-Regular.ttf (SIL OFL).
public static class ErstePdfBuilder
{
    public const double PageWidth = 594.96;
    public const double PageHeight = 841.92;

    // Column edges (operation date | operation | amount | balance).
    public static readonly IReadOnlyList<double> ColumnEdges = [6.75, 123.00, 402.75, 495.75, 588.75];

    public const double HeaderHeight = 15.00;
    public const double RowHeight = 30.00;
    public const double SeparatorHeight = 0.75;
    public const double TextSize = 12.0;
    public const double LabelSize = 10.0;

    // Lowest allowed bottom edge of the table; below it the footer starts.
    public const double MinTableBottom = 100;

    // Polish month abbreviations as printed in dates ("28 wrz 2026", "01 paź 2026").
    public static readonly IReadOnlyList<string> MonthAbbreviations =
        ["sty", "lut", "mar", "kwi", "maj", "cze", "lip", "sie", "wrz", "paź", "lis", "gru"];

    private const double FirstPageHeaderTop = 724.17;
    private const double LaterPageShift = 6.0;
    private const double TitleBaselineAboveHeader = 15.75;
    private const double TitleBoxAboveHeader = 1.12;
    private const double TitleBoxHeight = 39.0;
    private const double AccountBaselineAboveHeader = 103.5;
    private const double BorderLeft = 6.37;
    private const double BorderRight = 589.12;
    private const double BorderMargin = 0.37;
    private const double HeaderTextBaseline = 4.5;
    private const double RowBaseline = 11.25;
    private const double LabelBaseline = 21.75;
    private const double SecondLineBaseline = 23.25;
    private const double SecondLineLeft = 125.39;
    private const double DateLeft = 9.37;
    private const double OperationLeft = 125.77;
    private const double AmountRight = 493.36;
    private const double BalanceRight = 586.50;
    private const double FooterBelowTable = 18.0;
    private const double FooterRight = 574.50;

    private static readonly (byte R, byte G, byte B) Black = (0, 0, 0);
    private static readonly (byte R, byte G, byte B) Gray = (234, 234, 234);

    private static readonly Lazy<byte[]> FontBytes = new(
        () => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "fonts", "NotoSans-Regular.ttf")));

    public static byte[] Build(ErstePdfHeader header, IReadOnlyList<ErstePdfRow> rows, ErstePdfLayout? layout = null)
    {
        layout ??= new ErstePdfLayout();
        if (layout.FirstPageRows < 1 || layout.RowsPerPage < 1)
            throw new ArgumentException("Rows per page must be positive.", nameof(layout));

        var pages = Paginate(rows, layout);

        using var builder = new PdfDocumentBuilder();
        // Fixed metadata keeps regenerated PDFs stable.
        builder.DocumentInformation.Title = "Lista transakcji (dane testowe)";
        builder.DocumentInformation.Creator = "MyFinances.Api.Tests";
        builder.DocumentInformation.Producer = "MyFinances.Api.Tests";
        builder.DocumentInformation.CreationDate = "D:20261001000000Z";
        builder.DocumentInformation.ModifiedDate = "D:20261001000000Z";
        var font = builder.AddTrueTypeFont(FontBytes.Value);

        for (var pageIndex = 0; pageIndex < pages.Count; pageIndex++)
        {
            var painter = new Painter(builder.AddPage(PageWidth, PageHeight), font);
            var headerTop = FirstPageHeaderTop + (pageIndex == 0 ? 0 : LaterPageShift);

            if (!layout.OmitAccountLine)
                painter.Text($"Konto: {header.AccountNumber}", 181.05, headerTop + AccountBaselineAboveHeader, TextSize);
            if (!layout.OmitTitle)
            {
                painter.StrokeRect(BorderLeft, headerTop + TitleBoxAboveHeader, BorderRight - BorderLeft, TitleBoxHeight);
                painter.Text("Lista transakcji", 7.50, headerTop + TitleBaselineAboveHeader, 13.0);
            }

            var y = layout.OmitHeaderOnPage == pageIndex + 1 ? headerTop - HeaderHeight : DrawHeaderBlock(painter, headerTop);
            foreach (var row in pages[pageIndex])
                y = DrawRow(painter, y, row);

            if (y < MinTableBottom)
                throw new InvalidOperationException($"The rows on page {pageIndex + 1} run into the footer; use more pages.");

            painter.StrokeRect(BorderLeft, y - BorderMargin, BorderRight - BorderLeft, headerTop + BorderMargin - (y - BorderMargin));
            DrawFooter(painter, header, pageIndex + 1, pages.Count, y - FooterBelowTable);
        }

        var pdf = builder.Build();
        VeloBankPdfBuilder.PinTrailerId(pdf);
        return pdf;
    }

    // "-1 014,84": comma decimals and a real space as the thousands separator, as in the real files.
    public static string FormatMoney(decimal value) => VeloBankPdfBuilder.FormatMoney(value);

    // "28 wrz 2026": day, Polish month abbreviation, year, one string with real spaces.
    public static string FormatDate(DateOnly date) =>
        string.Create(CultureInfo.InvariantCulture, $"{date.Day:00} {MonthAbbreviations[date.Month - 1]} {date.Year}");

    private static List<List<ErstePdfRow>> Paginate(IReadOnlyList<ErstePdfRow> rows, ErstePdfLayout layout)
    {
        var pages = new List<List<ErstePdfRow>> { rows.Take(layout.FirstPageRows).ToList() };
        for (var next = layout.FirstPageRows; next < rows.Count; next += layout.RowsPerPage)
            pages.Add(rows.Skip(next).Take(layout.RowsPerPage).ToList());
        return pages;
    }

    // Draws the four grey 15 pt header cells and their texts; returns the bottom edge of the block.
    private static double DrawHeaderBlock(Painter p, double top)
    {
        var bottom = top - HeaderHeight;
        for (var c = 0; c < 4; c++)
            p.Rect(ColumnEdges[c], bottom, ColumnEdges[c + 1] - ColumnEdges[c], HeaderHeight);

        p.Text("Data operacji", DateLeft, bottom + HeaderTextBaseline, TextSize);
        p.Text("Operacja", OperationLeft, bottom + HeaderTextBaseline, TextSize);
        p.TextRight("Kwota", AmountRight, bottom + HeaderTextBaseline, TextSize);
        p.TextRight("Saldo", BalanceRight, bottom + HeaderTextBaseline, TextSize);
        return bottom;
    }

    // Draws one 30 pt row band: the date cell with its empty "Data księgowania" label, the operation
    // lines, amount and balance, and the separator (one thin piece per column) that closes the band.
    // Returns the bottom edge of the band.
    private static double DrawRow(Painter p, double top, ErstePdfRow row)
    {
        p.Text(row.DateText ?? FormatDate(row.OperationDate), DateLeft, top - RowBaseline, TextSize);
        p.Text("Data księgowania", DateLeft, top - LabelBaseline, LabelSize);

        for (var i = 0; i < row.OperationLines.Count; i++)
        {
            p.Text(row.OperationLines[i], i == 0 ? OperationLeft : SecondLineLeft,
                top - (i == 0 ? RowBaseline : SecondLineBaseline), TextSize);
        }

        p.TextRight(row.AmountText ?? $"{FormatMoney(row.Amount)} {row.Currency}", AmountRight, top - RowBaseline, TextSize);
        p.TextRight(row.BalanceText ?? $"{FormatMoney(row.Balance)} PLN", BalanceRight, top - RowBaseline, TextSize);

        var bottom = top - RowHeight;
        for (var c = 0; c < 4; c++)
            p.Rect(ColumnEdges[c], bottom, ColumnEdges[c + 1] - ColumnEdges[c], SeparatorHeight);
        return bottom;
    }

    private static void DrawFooter(Painter p, ErstePdfHeader header, int page, int pageCount, double baseline)
    {
        p.Text($"Dokument z dnia: {FormatDate(header.DocumentDate)}", 6.00, baseline, TextSize);
        p.TextRight($"Strona {page} z {pageCount}", FooterRight, baseline, TextSize);
    }

    private sealed class Painter(PdfPageBuilder page, PdfDocumentBuilder.AddedFont font)
    {
        // A thin filled rectangle (header cell or separator piece). PdfPig's writer cannot fill
        // without stroking, so the shape also carries a zero-width outline in the same grey.
        public void Rect(double x, double y, double width, double height)
        {
            page.SetStrokeColor(Gray.R, Gray.G, Gray.B);
            page.SetTextAndFillColor(Gray.R, Gray.G, Gray.B);
            page.DrawRectangle(new PdfPoint(x, y), width, height, 0, true);
        }

        // An outline only (the table border and the title box).
        public void StrokeRect(double x, double y, double width, double height)
        {
            page.SetStrokeColor(Gray.R, Gray.G, Gray.B);
            page.DrawRectangle(new PdfPoint(x, y), width, height, 0.75);
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
