using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;

namespace MyFinances.Api.Import;

// Parses the real mBank CSV export shape: cp1250 encoding (or the UTF-8-remojibaked variant,
// see DecodeMBankText), ';' delimiter, a variable-length preamble before the real header, Polish
// number/date formats, single-quote-wrapped account numbers. See
// context/changes/mbank-import-with-dedup/plan.md (Phase 2) for the format notes this was
// written against.
public class MBankCsvParser : IBankStatementParser
{
    private const string HeaderPrefix = "#Data księgowania";
    private static readonly string[] DateFormats = ["yyyy-MM-dd", "dd.MM.yyyy"];
    private static readonly Encoding Cp1250 = GetEncoding(1250);
    private static readonly Encoding Win1252Strict = GetEncoding(1252, strict: true);
    private static readonly CultureInfo PlPl = CultureInfo.GetCultureInfo("pl-PL");

    // Program.cs registers CodePagesEncodingProvider for the running app, but plain unit tests
    // (this class instantiated directly, no WebApplicationFactory/Program startup) never run
    // that registration. Register defensively here too — safe to call more than once.
    private static Encoding GetEncoding(int codePage, bool strict = false)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return strict
            ? Encoding.GetEncoding(codePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)
            : Encoding.GetEncoding(codePage);
    }

    public string BankName => "mBank";

    // Some mBank exports arrive as genuine cp1250 bytes; others have been round-tripped through
    // a tool that decoded the original cp1250 bytes as Windows-1252 and re-saved as UTF-8. Undo
    // that by decoding as UTF-8, then re-encoding via Windows-1252 (its exact inverse — this also
    // correctly reconstructs cp1250 bytes in the 0x80-0x9F range, e.g. 'Œ'/'Ÿ' -> 0x8C/0x9F, which
    // a naive "cast the char to a byte" reversal gets wrong since win1252 maps that byte range to
    // scattered code points, not code point == byte value). If either step fails, the bytes were
    // never remojibaked — decode as cp1250 directly instead.
    private static string DecodeMBankText(byte[] bytes)
    {
        try
        {
            var utf8Strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            var decoded = utf8Strict.GetString(bytes);
            var rawBytes = Win1252Strict.GetBytes(decoded);
            return Cp1250.GetString(rawBytes);
        }
        catch (Exception ex) when (ex is DecoderFallbackException or EncoderFallbackException)
        {
            return Cp1250.GetString(bytes);
        }
    }

    private static string ReadAllText(Stream fileStream)
    {
        using var buffer = new MemoryStream();
        fileStream.CopyTo(buffer);
        return NormalizeLines(DecodeMBankText(buffer.ToArray()));
    }

    // Some mBank exports have been round-tripped through a spreadsheet tool that re-saved the
    // real ';'-delimited row as a single, ','-delimited CSV cell — quoting it (and doubling its
    // internal '"' chars) whenever the row itself contains a quoted sub-field, then padding it
    // with a couple of empty trailing ',' cells. A row in that shape starts with a literal '"';
    // a plain, never-wrapped row (all existing real samples) never does, so unwrapping is safe to
    // attempt unconditionally per line.
    private static string NormalizeLines(string text)
    {
        using var reader = new StringReader(text);
        var sb = new StringBuilder();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            sb.AppendLine(UnwrapCommaPaddedLine(line));
        }

        return sb.ToString();
    }

    private static string UnwrapCommaPaddedLine(string line)
    {
        if (!line.StartsWith('"'))
        {
            return line;
        }

        var end = line.Length - 1;
        while (end >= 0 && line[end] == ',')
        {
            end--;
        }

        if (end < 1 || line[end] != '"')
        {
            return line;
        }

        var inner = line.Substring(1, end - 1);
        return inner.Replace("\"\"", "\"");
    }

    public bool CanParse(Stream fileStream)
    {
        var startPosition = fileStream.CanSeek ? fileStream.Position : 0;

        using var reader = new StringReader(ReadAllText(fileStream));
        string? line;
        var found = false;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.StartsWith(HeaderPrefix, StringComparison.Ordinal))
            {
                found = true;
                break;
            }
        }

        if (fileStream.CanSeek)
        {
            fileStream.Position = startPosition;
        }

        return found;
    }

    public ParseResult Parse(Stream fileStream)
    {
        // Non-obvious: preamble-skip needs a raw line scan before CsvReader ever sees the
        // stream, since CsvReader has no concept of "skip until this header appears".
        using var reader = new StringReader(ReadAllText(fileStream));
        string? line;
        while ((line = reader.ReadLine()) is not null && !line.StartsWith(HeaderPrefix, StringComparison.Ordinal))
        {
        }

        var transactions = new List<NormalizedTransaction>();
        var skippedErrorCount = 0;

        var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = ";",
            HasHeaderRecord = false,
            MissingFieldFound = null,
            BadDataFound = null,
        };

        using var csv = new CsvReader(reader, csvConfig);
        while (csv.Read())
        {
            var rawDate = csv.GetField(0);
            if (!DateOnly.TryParseExact(rawDate, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                // A row whose Data księgowania isn't a parseable date is the footer/blank-line
                // stop condition: this row and everything after it is not transaction data and
                // is not counted as an error.
                break;
            }

            try
            {
                var description = csv.GetField(3) ?? string.Empty;
                var rawAmount = csv.GetField(6) ?? string.Empty;
                var amount = decimal.Parse(rawAmount, NumberStyles.Number | NumberStyles.AllowLeadingSign, PlPl);

                transactions.Add(new NormalizedTransaction(date, description, amount));
            }
            catch (Exception ex) when (ex is FormatException or OverflowException)
            {
                // Row-level parse failure (e.g. malformed Kwota) on an otherwise-valid-date row:
                // skip it and keep going, don't abort the rest of the file.
                skippedErrorCount++;
            }
        }

        return new ParseResult(transactions, skippedErrorCount);
    }
}
