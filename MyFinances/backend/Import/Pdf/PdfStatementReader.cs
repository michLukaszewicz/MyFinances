using System.Globalization;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;

namespace MyFinances.Api.Import.Pdf;

// A word as PdfPig extracts it: its text, the centre of its bounding box and the baseline of its first letter.
public sealed record PdfWord(string Text, double CenterX, double CenterY, double Baseline);

// A drawn shape reduced to its bounding rectangle and how it was painted. Filled rectangles are
// how the table rules and header cells of VeloBank and Erste are drawn; mBank draws stroked lines.
public sealed record PdfShape(PdfRectangle Bounds, bool IsFilled, bool IsStroked);

public sealed record PdfPageContent(int Number, IReadOnlyList<PdfWord> Words, IReadOnlyList<PdfShape> Shapes);

// The bank-independent PDF mechanics shared by every PDF statement parser, so the banks cannot
// drift on error handling: one PdfPig access pattern behind one catch-all, one page model, one way
// to join cells, parse numbers and word a PII-free rejection. Recognition, table geometry, row
// reading and integrity checks stay in the bank's parser.
public static class PdfStatementReader
{
    // Digit runs are bounded so an absurd number is an unreadable cell, never an OverflowException.
    // Comma decimals, optional sign, a real space between thousands: "-82,30", "1 014,84".
    public const string NumberPattern = @"[-+]?(?:[0-9]{1,3}(?: [0-9]{3}){1,5}|[0-9]{1,15}),[0-9]{2}";

    private static readonly byte[] PdfMagic = "%PDF-"u8.ToArray();
    private static readonly CultureInfo PlPl = CultureInfo.GetCultureInfo("pl-PL");

    // Everything that touches PdfPig sits behind this one catch-all: Open throws on corrupt or
    // password-protected files, and GetPage/GetWords can throw too (missing font, invalid font data).
    // Any such failure means "unreadable" (null); only integrity checks, which run later on the
    // extracted data, may throw. Returns null as well when the bytes are not a PDF, the PDF has too
    // many pages, or isRecognised rejects page 1. Pages after the first are only read when allPages
    // is set, so recognising an upload stays cheap. Never throws, restores a seekable stream's position.
    public static List<PdfPageContent>? TryRead(
        Stream fileStream, int maxPages, bool allPages, Func<PdfPageContent, bool> isRecognised)
    {
        var startPosition = fileStream.CanSeek ? fileStream.Position : 0;
        try
        {
            using var buffer = new MemoryStream();
            fileStream.CopyTo(buffer);
            return Extract(buffer.ToArray(), maxPages, allPages, isRecognised);
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

    // Opens the PDF from the given byte copy and reads its pages.
    private static List<PdfPageContent>? Extract(
        byte[] bytes, int maxPages, bool allPages, Func<PdfPageContent, bool> isRecognised)
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
        if (!isRecognised(first))
        {
            return null;
        }

        var pages = new List<PdfPageContent> { first };
        if (allPages)
        {
            for (var number = 2; number <= document.NumberOfPages; number++)
            {
                pages.Add(ReadPage(document.GetPage(number)));
            }
        }

        return pages;
    }

    private static PdfPageContent ReadPage(UglyToad.PdfPig.Content.Page page)
    {
        var words = page.GetWords()
            .Select(w => new PdfWord(
                w.Text,
                (w.BoundingBox.Left + w.BoundingBox.Right) / 2,
                (w.BoundingBox.Top + w.BoundingBox.Bottom) / 2,
                w.Letters.Count > 0 ? w.Letters[0].StartBaseLine.Y : w.BoundingBox.Bottom))
            .ToList();

        var shapes = page.Paths
            .Select(p => (Bounds: p.GetBoundingRectangle(), p.IsFilled, p.IsStroked))
            .Where(s => s.Bounds.HasValue)
            .Select(s => new PdfShape(s.Bounds!.Value, s.IsFilled, s.IsStroked))
            .ToList();

        return new PdfPageContent(page.Number, words, shapes);
    }

    // Reassembles a cell: words are grouped into lines by baseline (top line first, lines closer
    // than lineTolerance are one line), each line is read left to right, and everything is joined
    // with single spaces.
    public static string JoinCell(IEnumerable<PdfWord> cellWords, double lineTolerance)
    {
        var lines = new List<List<PdfWord>>();
        foreach (var word in cellWords.OrderByDescending(w => w.Baseline))
        {
            if (lines.Count > 0 && lines[^1][0].Baseline - word.Baseline <= lineTolerance)
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

    // Parses text already matched by a pattern built from NumberPattern (comma decimals, thousands spaces).
    public static decimal ParseNumber(string text) =>
        decimal.Parse(text.Replace(" ", string.Empty), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, PlPl);

    // The message reaches the user as-is: the failing check, the page and (when readable) the row's
    // transaction date, never an amount, description or name.
    public static StatementIntegrityException Rejection(string bankName, string problem, int page, DateOnly? date)
    {
        var where = date is { } transactionDate
            ? $"page {page}, transaction date {transactionDate.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)}"
            : $"page {page}";
        return new StatementIntegrityException(
            $"{bankName} statement rejected: {problem} ({where}). The PDF may be incomplete, modified or in a layout this app does not support.");
    }
}
