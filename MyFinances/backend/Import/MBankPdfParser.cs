using System.Globalization;
using System.Text.RegularExpressions;
using MyFinances.Api.Import.Pdf;
using UglyToad.PdfPig.Core;

namespace MyFinances.Api.Import;

// Parses the mBank "Elektroniczne zestawienie operacji" PDF. The operations table is a grid of
// stroked lines with five columns (booking date | operation date | description | amount | balance
// after); the column edges are recovered from the horizontal segments (five contiguous segments on
// one y), and a row is the band between two consecutive lines of the first column. The date line of
// a row carries both dates, the amount and the balance, the description continues on the lines below
// it. Amounts and balances print a real space as the thousands separator and no currency (the
// statement currency is printed once, as "Waluta"), so every cell is assembled from its words.
//
// Three figures of the statement are independent of the rows and are all checked: the opening
// balance ("Saldo początkowe"), the closing balance ("Saldo końcowe") and the turnover summary
// (credits, debits and total as count and sum). A statement that fails any of them, or that has a row
// that cannot be read, is rejected with a StatementIntegrityException instead of being imported.
//
// Date is the booking date. A statement whose currency is not PLN is read and checked like any
// other, but yields no transactions: every row is counted as skipped (the app is PLN-only).
public class MBankPdfParser(int maxPages = MBankPdfParser.DefaultMaxPages) : IBankStatementParser
{
    public const int DefaultMaxPages = 200;

    private const string PlnCurrency = "PLN";
    private const int ColumnCount = 5;

    private const int BookingDateColumn = 0;
    private const int DescriptionColumn = 2;
    private const int AmountColumn = 3;
    private const int BalanceColumn = 4;

    // Grid lines are hairlines; a segment is horizontal when it is at most this tall.
    private const double MaxLineHeight = 1.0;
    private const double MinLineWidth = 5.0;

    // Segments of one grid row share their y within a hair; column edges meet within about a point.
    private const double SameLineTolerance = 0.2;
    private const double EdgeTolerance = 1.5;

    // Lines of one description cell are ~9.6 pt apart; words of one printed line share a baseline.
    private const double LineTolerance = 2.0;

    // The failing checks, as named in the integrity messages.
    private const string UnreadableRow = "a table row could not be read";
    private const string BalanceMismatch = "the running balance does not add up";
    private const string ClosingBalanceMismatch = "the closing balance differs from the balance of the last row";
    private const string SummaryMismatch = "the turnover summary differs from the rows";
    private const string MissingOpeningBalance = "the opening balance is missing";
    private const string MissingClosingBalance = "the closing balance is missing";
    private const string MissingSummary = "the turnover summary is missing";
    private const string MissingCurrency = "the statement currency is missing";

    // Words that page 1 of every mBank statement shows: the title, the bank name (letterhead) and the table header.
    private static readonly string[] RecognitionWords =
        ["Elektroniczne", "zestawienie", "operacji", "mBank", "księgowania", "Opis", "Kwota", "Saldo"];

    private static readonly Regex NumberOnly = new(
        "^" + PdfStatementReader.NumberPattern + "$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex Digits = new("^[0-9]{1,6}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public string BankName => "mBank";

    public StatementFormat Format => StatementFormat.Pdf;

    public bool CanParse(Stream fileStream) => TryRead(fileStream, allPages: false) is not null;

    public ParseResult Parse(Stream fileStream)
    {
        // The endpoint calls Parse on a manually chosen bank even when CanParse rejected the file.
        var pages = TryRead(fileStream, allPages: true);
        if (pages is null)
        {
            return new ParseResult([], 0);
        }

        var rows = new List<StatementRow>();
        foreach (var page in pages)
        {
            var edges = FindColumnEdges(page);
            if (edges is null)
            {
                // Page 1 was recognised by its words only; without the table it is no statement
                // this parser can read. A later page without one would silently lose its rows.
                if (page == pages[0])
                {
                    return new ParseResult([], 0);
                }

                throw Rejection(UnreadableRow, page.Number, null);
            }

            rows.AddRange(ReadRows(page, edges));
        }

        var currency = ReadCurrency(pages[0]) ?? throw Rejection(MissingCurrency, 1, null);
        var opening = ReadBalance(pages[0], "początkowe:") ?? throw Rejection(MissingOpeningBalance, 1, null);
        var closing = ReadClosingBalance(pages) ?? throw Rejection(MissingClosingBalance, pages[^1].Number, null);
        var summary = ReadSummary(pages[0]) ?? throw Rejection(MissingSummary, 1, null);

        CheckIntegrity(rows, opening, closing, summary, pages[^1].Number);

        if (currency != PlnCurrency)
        {
            return new ParseResult([], rows.Count);
        }

        var transactions = rows.Select(r => new NormalizedTransaction(r.Date, r.Description, r.Amount)).ToList();
        return new ParseResult(transactions, 0);
    }

    // Page 1 is only recognised as an mBank statement when it shows every recognition word.
    private List<PdfPageContent>? TryRead(Stream fileStream, bool allPages) =>
        PdfStatementReader.TryRead(fileStream, maxPages, allPages, LooksLikeMBankStatement);

    private static bool LooksLikeMBankStatement(PdfPageContent firstPage)
    {
        var texts = firstPage.Words.Select(w => w.Text).ToHashSet(StringComparer.Ordinal);
        return RecognitionWords.All(texts.Contains);
    }

    private static List<PdfRectangle> HorizontalLines(PdfPageContent page) =>
        page.Shapes.Select(s => s.Bounds)
            .Where(b => b.Height <= MaxLineHeight && b.Width >= MinLineWidth)
            .ToList();

    // The table's column edges: five contiguous horizontal segments on one y (a row or header line of
    // the grid; the summary table has three segments and the balance cells one).
    private static double[]? FindColumnEdges(PdfPageContent page)
    {
        foreach (var line in HorizontalLines(page).GroupBy(b => Math.Round(b.Top / SameLineTolerance)))
        {
            var segments = line.OrderBy(b => b.Left).ToList();
            if (segments.Count != ColumnCount)
            {
                continue;
            }

            var contiguous = true;
            for (var i = 0; i < ColumnCount - 1 && contiguous; i++)
            {
                contiguous = Math.Abs(segments[i].Right - segments[i + 1].Left) <= EdgeTolerance;
            }

            if (contiguous)
            {
                return segments.Select(s => s.Left).Append(segments[^1].Right).ToArray();
            }
        }

        return null;
    }

    // Rows are the bands between consecutive horizontal lines of the first column that hold text;
    // the header band (repeated on every page) is the one whose amount cell reads "Kwota".
    // Everything outside the table — letterhead, summary, balances, footer — lies outside these bands.
    private static List<StatementRow> ReadRows(PdfPageContent page, double[] edges)
    {
        var lines = HorizontalLines(page)
            .Where(b => Math.Abs(b.Left - edges[0]) <= EdgeTolerance && Math.Abs(b.Right - edges[1]) <= EdgeTolerance)
            .Select(b => b.Top)
            .Distinct()
            .OrderByDescending(y => y)
            .ToList();

        var rows = new List<StatementRow>();
        for (var i = 0; i + 1 < lines.Count; i++)
        {
            var top = lines[i];
            var bottom = lines[i + 1];
            var inBand = page.Words.Where(w => w.CenterY < top && w.CenterY > bottom).ToList();
            if (inBand.Count == 0)
            {
                continue;
            }

            var cells = new string[ColumnCount];
            for (var column = 0; column < ColumnCount; column++)
            {
                cells[column] = PdfStatementReader.JoinCell(inBand.Where(w => ColumnOf(edges, w.CenterX) == column), LineTolerance);
            }

            if (cells[AmountColumn] == "Kwota")
            {
                continue;
            }

            rows.Add(ReadRow(page.Number, cells));
        }

        return rows;
    }

    private static int ColumnOf(double[] edges, double x)
    {
        if (x < edges[0] - EdgeTolerance || x > edges[ColumnCount] + EdgeTolerance)
        {
            return -1;
        }

        for (var column = 0; column < ColumnCount - 1; column++)
        {
            if (x < edges[column + 1])
            {
                return column;
            }
        }

        return ColumnCount - 1;
    }

    // A band inside the table that cannot be read is an integrity failure, not a skipped row:
    // dropping it silently would hide a missing transaction.
    private static StatementRow ReadRow(int page, string[] cells)
    {
        if (!TryParseDate(cells[BookingDateColumn], out var date))
        {
            throw Rejection(UnreadableRow, page, null);
        }

        if (!TryParseNumber(cells[AmountColumn], out var amount) || !TryParseNumber(cells[BalanceColumn], out var balance))
        {
            throw Rejection(UnreadableRow, page, date);
        }

        return new StatementRow(page, date, cells[DescriptionColumn], amount, balance);
    }

    private static bool TryParseDate(string text, out DateOnly date) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    // "-82,30", "1 014,84": comma decimals, a space between thousands, no currency.
    private static bool TryParseNumber(string text, out decimal value)
    {
        if (!NumberOnly.IsMatch(text))
        {
            value = 0;
            return false;
        }

        value = PdfStatementReader.ParseNumber(text);
        return true;
    }

    // The words that share a printed line with the given word, to its right, left to right.
    private static IEnumerable<PdfWord> RestOfLine(PdfPageContent page, PdfWord anchor) =>
        page.Words
            .Where(w => w != anchor && Math.Abs(w.Baseline - anchor.Baseline) <= LineTolerance && w.CenterX > anchor.CenterX)
            .OrderBy(w => w.CenterX);

    // "Waluta  PLN" in the account block.
    private static string? ReadCurrency(PdfPageContent page)
    {
        foreach (var label in page.Words.Where(w => w.Text == "Waluta"))
        {
            var value = string.Join(" ", RestOfLine(page, label).Select(w => w.Text));
            if (value.Length > 0)
            {
                return value;
            }
        }

        return null;
    }

    // "Saldo początkowe: 2 100,00": the number is everything to the right of the colon word. The
    // label is "Saldo" immediately followed by the given word, so a stray word elsewhere is not taken.
    private static decimal? ReadBalance(PdfPageContent page, string labelWord)
    {
        foreach (var label in page.Words.Where(w => w.Text == labelWord))
        {
            var hasSaldo = page.Words.Any(w => w.Text == "Saldo" && Math.Abs(w.Baseline - label.Baseline) <= LineTolerance && w.CenterX < label.CenterX);
            if (!hasSaldo)
            {
                continue;
            }

            var text = string.Join(" ", RestOfLine(page, label).Select(w => w.Text));
            if (TryParseNumber(text, out var value))
            {
                return value;
            }
        }

        return null;
    }

    // Printed beside the last row, so only the last page that carries it counts.
    private static decimal? ReadClosingBalance(IReadOnlyList<PdfPageContent> pages)
    {
        for (var i = pages.Count - 1; i >= 0; i--)
        {
            if (ReadBalance(pages[i], "końcowe:") is { } value)
            {
                return value;
            }
        }

        return null;
    }

    // "Uznania | 7 | 4 274,36": count and sum on the label's line; debits print as positive sums.
    private static Summary? ReadSummary(PdfPageContent page)
    {
        var credits = ReadSummaryLine(page, "Uznania");
        var debits = ReadSummaryLine(page, "Obciążenia");
        var total = ReadSummaryLine(page, "Łącznie");
        return credits is null || debits is null || total is null ? null : new Summary(credits.Value, debits.Value, total.Value);
    }

    private static (int Count, decimal Sum)? ReadSummaryLine(PdfPageContent page, string label)
    {
        var anchor = page.Words.FirstOrDefault(w => w.Text == label);
        if (anchor is null)
        {
            return null;
        }

        var rest = RestOfLine(page, anchor).ToList();
        if (rest.Count < 2 || !Digits.IsMatch(rest[0].Text))
        {
            return null;
        }

        var sum = string.Join(" ", rest.Skip(1).Select(w => w.Text));
        return TryParseNumber(sum, out var value)
            ? (int.Parse(rest[0].Text, CultureInfo.InvariantCulture), value)
            : null;
    }

    // Checks run in file order, so the first failure reported is the first one in the statement.
    private static void CheckIntegrity(
        IReadOnlyList<StatementRow> rows, decimal opening, decimal closing, Summary summary, int lastPage)
    {
        var expected = opening;
        foreach (var row in rows)
        {
            expected += row.Amount;
            if (row.Balance != expected)
            {
                throw Rejection(BalanceMismatch, row.Page, row.Date);
            }
        }

        if (closing != (rows.Count > 0 ? rows[^1].Balance : opening))
        {
            throw Rejection(ClosingBalanceMismatch, lastPage, null);
        }

        var credits = rows.Where(r => r.Amount > 0).ToList();
        var debits = rows.Where(r => r.Amount < 0).ToList();
        var matches =
            summary.Credits == (credits.Count, credits.Sum(r => r.Amount))
            && summary.Debits == (debits.Count, -debits.Sum(r => r.Amount))
            && summary.Total == (rows.Count, rows.Sum(r => r.Amount));
        if (!matches)
        {
            throw Rejection(SummaryMismatch, 1, null);
        }
    }

    private static StatementIntegrityException Rejection(string problem, int page, DateOnly? date) =>
        PdfStatementReader.Rejection("mBank", problem, page, date);

    private sealed record Summary((int Count, decimal Sum) Credits, (int Count, decimal Sum) Debits, (int Count, decimal Sum) Total);

    private sealed record StatementRow(int Page, DateOnly Date, string Description, decimal Amount, decimal Balance);
}
