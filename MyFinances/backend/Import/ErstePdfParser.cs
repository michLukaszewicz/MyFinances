using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MyFinances.Api.Import.Pdf;
using UglyToad.PdfPig.Core;

namespace MyFinances.Api.Import;

// Parses the Erste Bank Polska "Lista transakcji" PDF (Chromium/Skia output). The table is drawn,
// not marked up: four columns (operation date | operation | amount | balance after) whose header
// cells are four filled rectangles, and one thin filled rectangle per column closing every 30 pt row
// band. Rows therefore come from the bands between the separators of the first column, never from
// text baselines. Amounts and balances print "-0,10 PLN" with a real space as the thousands separator
// ("1 098,64 PLN" is three words), so every cell is assembled from its words.
//
// The extracted text never names the bank, so recognition is a composite: the title, the "Konto:"
// line, the table header words and at least one table header block of cells on page 1.
//
// Date is the operation date (the only date the PDF carries; the "Data księgowania" label under it has
// no value). Rows are printed newest booking first, and same-day rows are NOT printed in booking
// order, so the printed balances only link up order-independently: over the PLN rows the multiset of
// "balance before" (balance - amount) must equal the multiset of "balance after" apart from exactly one
// value on each side (the oldest row's before-balance and the newest row's after-balance). A missing
// oldest or newest row is therefore invisible to the check — a documented blind spot. A foreign-currency
// row is skipped, counted, and disables the check for the file (its effect on the printed balance is
// unverified). A statement that fails the check, or has a row that cannot be read, is rejected with a
// StatementIntegrityException instead of being imported.
public class ErstePdfParser(int maxPages = ErstePdfParser.DefaultMaxPages) : IBankStatementParser
{
    public const int DefaultMaxPages = 200;

    private const string PlnCurrency = "PLN";
    private const int ColumnCount = 4;

    private const int DateColumn = 0;
    private const int OperationColumn = 1;
    private const int AmountColumn = 2;
    private const int BalanceColumn = 3;

    // Header cells are 15 pt tall filled rectangles.
    private const double MinHeaderCellHeight = 10;
    private const double MaxHeaderCellHeight = 20;
    private const double MinHeaderCellWidth = 20;

    // Row separators are 0.75 pt tall.
    private const double MinSeparatorHeight = 0.3;
    private const double MaxSeparatorHeight = 3;

    // Cells of one header block share their top edge and meet within a hair; lines of one cell are ~12 pt apart
    // (the date and its label are ~10 pt apart, the label is a smaller line of the same cell).
    private const double EdgeTolerance = 1.5;
    private const double LineTolerance = 2.0;

    // The failing checks, as named in the integrity messages.
    private const string UnreadableRow = "a table row could not be read";
    private const string BalanceMismatch = "the balances do not link up";

    // Words that page 1 of every Erste statement shows: the title, the account line and the table header.
    private static readonly string[] RecognitionWords =
        ["Lista", "transakcji", "Konto:", "Data", "operacji", "Operacja", "Kwota", "Saldo"];

    // Polish month abbreviations with diacritics folded, as they appear in "28 wrz 2026" and "01 paź 2026".
    private static readonly string[] FoldedMonthAbbreviations =
        ["sty", "lut", "mar", "kwi", "maj", "cze", "lip", "sie", "wrz", "paz", "lis", "gru"];

    private static readonly Regex MoneyPattern = new(
        "^(?<number>" + PdfStatementReader.NumberPattern + ") (?<currency>[A-Z]{3})$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex DatePattern = new(
        @"^(?<day>[0-9]{1,2}) (?<month>\S{1,10}) (?<year>[0-9]{4})$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public string BankName => "Erste";

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
        for (var i = 0; i < pages.Count; i++)
        {
            // Every page of a statement repeats the table header; a later page without one would
            // silently lose its rows, so it is an integrity failure (page 1 was recognised already).
            if (i > 0 && FindHeaderBlocks(pages[i]).Count == 0)
            {
                throw Rejection(UnreadableRow, pages[i].Number, null);
            }

            rows.AddRange(ReadRows(pages[i]));
        }

        CheckIntegrity(rows);

        var transactions = rows
            .Where(r => r.Currency == PlnCurrency)
            .Select(r => new NormalizedTransaction(r.Date, r.Description, r.Amount))
            .ToList();
        return new ParseResult(transactions, rows.Count - transactions.Count);
    }

    private List<PdfPageContent>? TryRead(Stream fileStream, bool allPages) =>
        PdfStatementReader.TryRead(fileStream, maxPages, allPages, LooksLikeErsteStatement);

    // Selected by IsFilled and bounding box only: the synthetic fixtures also stroke their
    // rectangles, the real files do not, and the real page carries unfilled clip rectangles,
    // a filled logo and stroked outline boxes that must not be mistaken for table parts.
    private static IEnumerable<PdfRectangle> FilledRectangles(PdfPageContent page) =>
        page.Shapes.Where(s => s.IsFilled).Select(s => s.Bounds);

    // Page 1 is only recognised as an Erste statement when it shows every recognition word and a table header block.
    private static bool LooksLikeErsteStatement(PdfPageContent firstPage)
    {
        var texts = firstPage.Words.Select(w => w.Text).ToHashSet(StringComparer.Ordinal);
        return RecognitionWords.All(texts.Contains) && FindHeaderBlocks(firstPage).Count > 0;
    }

    private static List<StatementRow> ReadRows(PdfPageContent page) =>
        FindBands(page).Select(band => ReadRow(page.Number, SplitIntoCells(page, band))).ToList();

    // Header blocks: four contiguous filled cells sharing one top edge.
    private static List<HeaderBlock> FindHeaderBlocks(PdfPageContent page)
    {
        var cells = FilledRectangles(page)
            .Where(r => r.Height >= MinHeaderCellHeight && r.Height <= MaxHeaderCellHeight && r.Width >= MinHeaderCellWidth)
            .OrderByDescending(r => r.Top)
            .ToList();

        var blocks = new List<HeaderBlock>();
        var start = 0;
        while (start < cells.Count)
        {
            var end = start + 1;
            while (end < cells.Count && Math.Abs(cells[end].Top - cells[start].Top) <= EdgeTolerance)
            {
                end++;
            }

            var block = TryBuildHeaderBlock(cells.GetRange(start, end - start));
            if (block is not null)
            {
                blocks.Add(block);
            }

            start = end;
        }

        return blocks;
    }

    private static HeaderBlock? TryBuildHeaderBlock(List<PdfRectangle> candidates)
    {
        // The real page paints each header cell twice (two near-identical fills a fraction of a point
        // apart), so cells that start at the same left edge collapse into one.
        var cells = new List<PdfRectangle>();
        foreach (var cell in candidates.OrderBy(c => c.Left))
        {
            if (cells.Count == 0 || cell.Left - cells[^1].Left > EdgeTolerance)
            {
                cells.Add(cell);
            }
        }

        if (cells.Count != ColumnCount)
        {
            return null;
        }

        for (var i = 0; i < ColumnCount - 1; i++)
        {
            if (Math.Abs(cells[i].Right - cells[i + 1].Left) > EdgeTolerance)
            {
                return null;
            }
        }

        var edges = cells.Select(c => c.Left).Append(cells[^1].Right).ToArray();
        return new HeaderBlock(cells.Max(c => c.Top), cells.Min(c => c.Bottom), edges);
    }

    // One thin filled piece per column closes each row; only the first-column piece is wanted,
    // found by its bounding box against the page's header cells.
    private static List<PdfRectangle> FindSeparators(PdfPageContent page, IReadOnlyList<HeaderBlock> headers) =>
        FilledRectangles(page)
            .Where(r => r.Height >= MinSeparatorHeight && r.Height <= MaxSeparatorHeight
                && headers.Any(h => Math.Abs(r.Left - h.Edges[0]) <= EdgeTolerance
                    && Math.Abs(r.Width - (h.Edges[1] - h.Edges[0])) <= EdgeTolerance))
            .ToList();

    // Walks the page top to bottom: a header block sets the column edges and the top of the next
    // band; each separator closes a band at its top edge and the next band starts at its bottom edge.
    // Whatever lies outside a band (title, account line, header texts, the footer under the last
    // separator) belongs to no row.
    private static List<Band> FindBands(PdfPageContent page)
    {
        var headers = FindHeaderBlocks(page);
        var dividers = headers.Select(h => new Divider(h.Top, h.Bottom, h))
            .Concat(FindSeparators(page, headers).Select(s => new Divider(s.Top, s.Bottom, null)))
            .OrderByDescending(d => d.Top);

        var bands = new List<Band>();
        double[]? edges = null;
        var bandTop = 0.0;
        foreach (var divider in dividers)
        {
            if (divider.Header is { } header)
            {
                edges = header.Edges;
                bandTop = header.Bottom;
            }
            else if (edges is not null)
            {
                bands.Add(new Band(bandTop, divider.Top, edges));
                bandTop = divider.Bottom;
            }
        }

        return bands;
    }

    // The date cell keeps only its first line (the operation date): under it sits the "Data księgowania"
    // label, a smaller line of the same cell that never carries a value.
    private static string[] SplitIntoCells(PdfPageContent page, Band band)
    {
        var inBand = page.Words.Where(w => w.CenterY < band.Top && w.CenterY > band.Bottom).ToList();
        var cells = new string[ColumnCount];
        for (var column = 0; column < ColumnCount; column++)
        {
            var words = inBand.Where(w => ColumnOf(band.Edges, w.CenterX) == column).ToList();
            if (column == DateColumn && words.Count > 0)
            {
                var firstBaseline = words.Max(w => w.Baseline);
                words = words.Where(w => firstBaseline - w.Baseline <= LineTolerance).ToList();
            }

            cells[column] = PdfStatementReader.JoinCell(words, LineTolerance);
        }

        return cells;
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

    // A band inside a recognised table that cannot be read is an integrity failure, not a skipped row:
    // dropping it silently would hide a missing transaction.
    private static StatementRow ReadRow(int page, string[] cells)
    {
        if (!TryParseDate(cells[DateColumn], out var date))
        {
            throw Rejection(UnreadableRow, page, null);
        }

        if (!TryParseMoney(cells[AmountColumn], out var amount, out var currency))
        {
            throw Rejection(UnreadableRow, page, date);
        }

        // Only PLN rows take part in the balance check, so only their balance has to be readable.
        decimal balance = 0;
        if (currency == PlnCurrency)
        {
            if (!TryParseMoney(cells[BalanceColumn], out balance, out var balanceCurrency) || balanceCurrency != PlnCurrency)
            {
                throw Rejection(UnreadableRow, page, date);
            }
        }

        return new StatementRow(page, date, cells[OperationColumn], amount, currency, balance);
    }

    // "28 wrz 2026", "01 paź 2026": day, Polish month abbreviation (diacritics folded), year.
    private static bool TryParseDate(string text, out DateOnly date)
    {
        date = default;
        var match = DatePattern.Match(text);
        if (!match.Success)
        {
            return false;
        }

        var month = Array.IndexOf(FoldedMonthAbbreviations, Fold(match.Groups["month"].Value)) + 1;
        var year = int.Parse(match.Groups["year"].Value, CultureInfo.InvariantCulture);
        var day = int.Parse(match.Groups["day"].Value, CultureInfo.InvariantCulture);
        if (month == 0 || year < 1 || day < 1 || day > DateTime.DaysInMonth(year, month))
        {
            return false;
        }

        date = new DateOnly(year, month, day);
        return true;
    }

    private static string Fold(string token)
    {
        var decomposed = token.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var folded = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                // "ł" and friends have no decomposition, but no month abbreviation uses them.
                folded.Append(c);
            }
        }

        return folded.ToString();
    }

    // "-0,10 PLN", "1 098,64 PLN": comma decimals, a space between thousands, the currency last.
    private static bool TryParseMoney(string text, out decimal amount, out string currency)
    {
        var match = MoneyPattern.Match(text);
        if (!match.Success)
        {
            amount = 0;
            currency = string.Empty;
            return false;
        }

        amount = PdfStatementReader.ParseNumber(match.Groups["number"].Value);
        currency = match.Groups["currency"].Value;
        return true;
    }

    // Order-independent linkage over the PLN rows. A foreign-currency row switches the check off:
    // its effect on the printed PLN balance is unverified, so nothing is compared.
    private static void CheckIntegrity(IReadOnlyList<StatementRow> rows)
    {
        if (rows.Any(r => r.Currency != PlnCurrency))
        {
            return;
        }

        // Every "balance before" that finds no equal "balance after" (and vice versa) is unmatched.
        var afterCounts = Counts(rows.Select(r => r.Balance));
        var unmatchedBefore = new List<int>();
        for (var i = 0; i < rows.Count; i++)
        {
            if (!Take(afterCounts, rows[i].Balance - rows[i].Amount))
            {
                unmatchedBefore.Add(i);
            }
        }

        var beforeCounts = Counts(rows.Select(r => r.Balance - r.Amount));
        var unmatchedAfter = new List<int>();
        for (var i = 0; i < rows.Count; i++)
        {
            if (!Take(beforeCounts, rows[i].Balance))
            {
                unmatchedAfter.Add(i);
            }
        }

        // Exactly the oldest row's before-balance and the newest row's after-balance stay unmatched.
        if (rows.Count == 0 || (unmatchedBefore.Count == 1 && unmatchedAfter.Count == 1))
        {
            return;
        }

        var culprit = rows[unmatchedBefore.Concat(unmatchedAfter).Min()];
        throw Rejection(BalanceMismatch, culprit.Page, culprit.Date);
    }

    private static Dictionary<decimal, int> Counts(IEnumerable<decimal> values)
    {
        var counts = new Dictionary<decimal, int>();
        foreach (var value in values)
        {
            counts[value] = counts.GetValueOrDefault(value) + 1;
        }

        return counts;
    }

    private static bool Take(Dictionary<decimal, int> counts, decimal value)
    {
        if (counts.GetValueOrDefault(value) == 0)
        {
            return false;
        }

        counts[value]--;
        return true;
    }

    private static StatementIntegrityException Rejection(string problem, int page, DateOnly? date) =>
        PdfStatementReader.Rejection("Erste", problem, page, date);

    private sealed record HeaderBlock(double Top, double Bottom, double[] Edges);

    private sealed record Divider(double Top, double Bottom, HeaderBlock? Header);

    // A row's vertical extent (Top above Bottom) and the column edges of the header block above it.
    private sealed record Band(double Top, double Bottom, double[] Edges);

    private sealed record StatementRow(
        int Page, DateOnly Date, string Description, decimal Amount, string Currency, decimal Balance);
}
