using System.Globalization;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;

namespace MyFinances.Api.Import;

// Parses the VeloBank "Historia rachunku" PDF (wkhtmltopdf output; the bank offers no CSV export).
// The table is drawn, not marked up: five columns (transaction date | booking date | description |
// amount | balance after) whose cells are grey filled rectangles in the header, and one thin filled
// rectangle per row under the first column. Rows therefore come from the bands between those
// separators — never from text baselines, because the description starts at the top of a tall band
// while date, amount and balance sit at their own offset (centred or a hair below the first line).
// Amounts print a real space as the thousands separator, so PdfPig returns "1 014,84 PLN" as three
// words and every cell is assembled from its words. See context/changes/pdf-statement-import/plan.md
// (Phase 4) for the design and the measurements this was written against.
//
// Date is the transaction date (a pending row has no booking date, and the dedup hash must not
// change once it is booked). Pending rows are returned like booked ones. Non-PLN rows are skipped.
// A statement whose printed running balances do not add up is rejected with a
// StatementIntegrityException instead of being imported.
public class VeloBankPdfParser(int maxPages = VeloBankPdfParser.DefaultMaxPages) : IBankStatementParser
{
    public const int DefaultMaxPages = 200;

    private const string PlnCurrency = "PLN";
    private const int ColumnCount = 5;

    private const int TransactionDateColumn = 0;
    private const int BookingDateColumn = 1;
    private const int DescriptionColumn = 2;
    private const int AmountColumn = 3;
    private const int BalanceColumn = 4;

    // Header cells are ~28 pt tall grey rectangles; their thin white dividers (0.58 pt wide) must not count.
    private const double MinHeaderCellHeight = 20;
    private const double MaxHeaderCellHeight = 40;
    private const double MinHeaderCellWidth = 20;

    // Row separators are ~1.2 pt tall.
    private const double MinSeparatorHeight = 0.3;
    private const double MaxSeparatorHeight = 3;

    // Column edges shift by ~0.58 pt between header blocks on one page; lines of one cell are ~5.8 pt apart.
    private const double EdgeTolerance = 1.5;
    private const double LineTolerance = 2.0;

    // The failing checks, as named in the integrity messages.
    private const string UnreadableRow = "a table row could not be read";
    private const string BalanceMismatch = "the running balance does not add up";
    private const string CardAmountMismatch = "the card amount in the description differs from the amount column";

    private static readonly byte[] PdfMagic = "%PDF-"u8.ToArray();
    private static readonly CultureInfo PlPl = CultureInfo.GetCultureInfo("pl-PL");

    // Words that page 1 of every VeloBank statement shows: the title, the bank name (footer) and the table header.
    private static readonly string[] RecognitionWords =
        ["Historia", "rachunku", "VeloBank", "TRANSAKCJI", "KSIĘGOWANIA", "KWOTA", "SALDO"];

    // Digit runs are bounded so an absurd number is an unreadable cell, never an OverflowException.
    private static readonly Regex MoneyPattern = new(
        @"^(?<number>[-+]?(?:[0-9]{1,3}(?: [0-9]{3}){1,5}|[0-9]{1,15}),[0-9]{2}) (?<currency>[A-Z]{3})$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // "Operacja kartą 0000 **** **** 0000 na kwotę 12,34 PLN w <merchant>..." — anchored at the start so a
    // free-text transfer title that happens to contain "na kwotę" is never mistaken for a card amount.
    private static readonly Regex CardAmountPattern = new(
        @"^Operacja kartą .*? na kwotę (?<number>(?:[0-9]{1,3}(?: [0-9]{3}){1,5}|[0-9]{1,15}),[0-9]{2}) (?<currency>[A-Z]{3})(?: |$)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public string BankName => "VeloBank";

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
            // Every page of a statement repeats the table header; a later page without one would
            // silently lose its rows, so it is an integrity failure (page 1 was recognised already).
            if (page != pages[0] && FindHeaderBlocks(page).Count == 0)
            {
                throw Rejection(UnreadableRow, page.Number, null);
            }

            rows.AddRange(ReadRows(page));
        }

        CheckIntegrity(rows);

        var transactions = rows
            .Where(r => r.Currency == PlnCurrency)
            .Select(r => new NormalizedTransaction(r.Date, r.Description, r.Amount))
            .ToList();
        return new ParseResult(transactions, rows.Count - transactions.Count);
    }

    // Everything that touches PdfPig sits behind this one catch-all: Open throws on corrupt or
    // password-protected files, and GetPage/GetWords can throw too (missing font, invalid font data).
    // Any such failure means "unreadable" (null); only the integrity checks, which run later on the
    // extracted data, may throw. Never throws, restores a seekable stream's position.
    private List<PageContent>? TryRead(Stream fileStream, bool allPages)
    {
        var startPosition = fileStream.CanSeek ? fileStream.Position : 0;
        try
        {
            using var buffer = new MemoryStream();
            fileStream.CopyTo(buffer);
            return Extract(buffer.ToArray(), allPages);
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            if (fileStream.CanSeek)
            {
                fileStream.Position = startPosition;
            }
        }
    }

    // Opens the PDF from the given byte copy and reads its pages, or returns null when the bytes are
    // not a PDF, the PDF has too many pages, or page 1 is not a VeloBank statement. Pages after the
    // first are only read when allPages is set, so recognising an upload stays cheap.
    private List<PageContent>? Extract(byte[] bytes, bool allPages)
    {
        if (!bytes.AsSpan().StartsWith(PdfMagic))
        {
            return null;
        }

        using var document = PdfDocument.Open(bytes);
        if (document.NumberOfPages < 1 || document.NumberOfPages > maxPages)
        {
            return null;
        }

        var first = ReadPage(document.GetPage(1));
        if (!LooksLikeVeloBankStatement(first))
        {
            return null;
        }

        var pages = new List<PageContent> { first };
        if (allPages)
        {
            for (var number = 2; number <= document.NumberOfPages; number++)
            {
                pages.Add(ReadPage(document.GetPage(number)));
            }
        }

        return pages;
    }

    private static PageContent ReadPage(Page page)
    {
        var words = page.GetWords()
            .Select(w => new PageWord(
                w.Text,
                (w.BoundingBox.Left + w.BoundingBox.Right) / 2,
                (w.BoundingBox.Top + w.BoundingBox.Bottom) / 2,
                w.Letters.Count > 0 ? w.Letters[0].StartBaseLine.Y : w.BoundingBox.Bottom))
            .ToList();

        // Selected by IsFilled and bounding box only: the synthetic fixtures also stroke their
        // rectangles, the real files do not.
        var filledRectangles = page.Paths
            .Where(p => p.IsFilled)
            .Select(p => p.GetBoundingRectangle())
            .Where(r => r.HasValue)
            .Select(r => r!.Value)
            .ToList();

        return new PageContent(page.Number, words, filledRectangles);
    }

    private static bool LooksLikeVeloBankStatement(PageContent firstPage)
    {
        var texts = firstPage.Words.Select(w => w.Text).ToHashSet(StringComparer.Ordinal);
        return RecognitionWords.All(texts.Contains);
    }

    private static List<StatementRow> ReadRows(PageContent page) =>
        FindBands(page).Select(band => ReadRow(page.Number, SplitIntoCells(page, band))).ToList();

    // Header blocks: five contiguous wide grey cells sharing one top edge (a repeated header block
    // mid-page is just another block, usually with slightly shifted edges).
    private static List<HeaderBlock> FindHeaderBlocks(PageContent page)
    {
        var cells = page.FilledRectangles
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

    private static HeaderBlock? TryBuildHeaderBlock(List<PdfRectangle> cells)
    {
        if (cells.Count != ColumnCount)
        {
            return null;
        }

        cells.Sort((a, b) => a.Left.CompareTo(b.Left));
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

    // One thin filled piece per row spans the first column (the other four columns have their own
    // pieces, and page 1 carries a few full-width rules around the title block). Only the first-column
    // piece is wanted, found by its bounding box against the page's header cells.
    private static List<PdfRectangle> FindSeparators(PageContent page, IReadOnlyList<HeaderBlock> headers) =>
        page.FilledRectangles
            .Where(r => r.Height >= MinSeparatorHeight && r.Height <= MaxSeparatorHeight
                && headers.Any(h => Math.Abs(r.Left - h.Edges[0]) <= EdgeTolerance
                    && Math.Abs(r.Width - (h.Edges[1] - h.Edges[0])) <= EdgeTolerance))
            .ToList();

    // Walks the page top to bottom: a header block sets the column edges and the top of the next
    // band; each separator closes a band at its top edge and the next band starts at its bottom edge.
    // Whatever lies outside a band (title block, header texts, the footer under the last separator)
    // belongs to no row.
    private static List<Band> FindBands(PageContent page)
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

    private static string[] SplitIntoCells(PageContent page, Band band)
    {
        var inBand = page.Words.Where(w => w.CenterY < band.Top && w.CenterY > band.Bottom).ToList();
        var cells = new string[ColumnCount];
        for (var column = 0; column < ColumnCount; column++)
        {
            cells[column] = JoinCell(inBand.Where(w => ColumnOf(band.Edges, w.CenterX) == column));
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

    // Reassembles a cell: words are grouped into lines by baseline (top line first), each line is
    // read left to right, and everything is joined with single spaces.
    private static string JoinCell(IEnumerable<PageWord> cellWords)
    {
        var lines = new List<List<PageWord>>();
        foreach (var word in cellWords.OrderByDescending(w => w.Baseline))
        {
            if (lines.Count > 0 && lines[^1][0].Baseline - word.Baseline <= LineTolerance)
            {
                lines[^1].Add(word);
            }
            else
            {
                lines.Add([word]);
            }
        }

        return string.Join(" ", lines.Select(line => string.Join(" ", line.OrderBy(w => w.CenterX).Select(w => w.Text))));
    }

    // A band inside a recognised table that cannot be read is an integrity failure, not a skipped row:
    // dropping it silently would hide a missing transaction.
    private static StatementRow ReadRow(int page, string[] cells)
    {
        if (!TryParseDate(cells[TransactionDateColumn], out var date))
        {
            throw Rejection(UnreadableRow, page, null);
        }

        var isPending = cells[BookingDateColumn] == "-";
        if (!isPending && !TryParseDate(cells[BookingDateColumn], out _))
        {
            throw Rejection(UnreadableRow, page, date);
        }

        if (!TryParseMoney(cells[AmountColumn], out var amount, out var currency))
        {
            throw Rejection(UnreadableRow, page, date);
        }

        // Only booked PLN rows take part in the balance chain, so only their balance has to be readable.
        decimal? balance = null;
        if (currency == PlnCurrency && !isPending)
        {
            if (!TryParseMoney(cells[BalanceColumn], out var parsedBalance, out var balanceCurrency) || balanceCurrency != PlnCurrency)
            {
                throw Rejection(UnreadableRow, page, date);
            }

            balance = parsedBalance;
        }

        return new StatementRow(page, date, cells[DescriptionColumn], amount, currency, isPending, balance);
    }

    private static bool TryParseDate(string text, out DateOnly date) =>
        DateOnly.TryParseExact(text, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    // "-82,30 PLN", "1 014,84 PLN": comma decimals, a space between thousands, the currency last.
    private static bool TryParseMoney(string text, out decimal amount, out string currency)
    {
        var match = MoneyPattern.Match(text);
        if (!match.Success)
        {
            amount = 0;
            currency = string.Empty;
            return false;
        }

        amount = ParseNumber(match.Groups["number"].Value);
        currency = match.Groups["currency"].Value;
        return true;
    }

    private static decimal ParseNumber(string text) =>
        decimal.Parse(text.Replace(" ", string.Empty), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, PlPl);

    // Checks run in file order, so the first failure reported is the first one in the statement.
    private static void CheckIntegrity(IReadOnlyList<StatementRow> rows)
    {
        StatementRow? previousBooked = null;
        foreach (var row in rows)
        {
            if (row.Currency != PlnCurrency)
            {
                // The effect of a foreign-currency row on the printed PLN balance is unverified, so
                // nothing is compared across it.
                previousBooked = null;
                continue;
            }

            CheckCardAmount(row);

            // Pending rows print no balance; they neither break nor extend the chain.
            if (row.IsPending)
            {
                continue;
            }

            // The list is newest first: this row's balance is the previous (newer) row's balance before its amount.
            if (previousBooked is not null && row.Balance != previousBooked.Balance - previousBooked.Amount)
            {
                throw Rejection(BalanceMismatch, row.Page, row.Date);
            }

            previousBooked = row;
        }
    }

    // The amount a card row repeats in its description must equal the amount column. A description
    // naming another currency (a foreign card payment booked in PLN) cannot be compared.
    private static void CheckCardAmount(StatementRow row)
    {
        var match = CardAmountPattern.Match(row.Description);
        if (!match.Success || match.Groups["currency"].Value != PlnCurrency)
        {
            return;
        }

        if (ParseNumber(match.Groups["number"].Value) != Math.Abs(row.Amount))
        {
            throw Rejection(CardAmountMismatch, row.Page, row.Date);
        }
    }

    // The message reaches the user as-is: the failing check, the page and (when readable) the row's
    // transaction date, never an amount, description or name.
    private static StatementIntegrityException Rejection(string problem, int page, DateOnly? date)
    {
        var where = date is { } transactionDate
            ? $"page {page}, transaction date {transactionDate.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)}"
            : $"page {page}";
        return new StatementIntegrityException(
            $"VeloBank statement rejected: {problem} ({where}). The PDF may be incomplete, modified or in a layout this app does not support.");
    }

    private sealed record PageWord(string Text, double CenterX, double CenterY, double Baseline);

    private sealed record PageContent(int Number, IReadOnlyList<PageWord> Words, IReadOnlyList<PdfRectangle> FilledRectangles);

    private sealed record HeaderBlock(double Top, double Bottom, double[] Edges);

    private sealed record Divider(double Top, double Bottom, HeaderBlock? Header);

    // A row's vertical extent (Top above Bottom) and the column edges of the header block above it.
    private sealed record Band(double Top, double Bottom, double[] Edges);

    private sealed record StatementRow(
        int Page, DateOnly Date, string Description, decimal Amount, string Currency, bool IsPending, decimal? Balance);
}
