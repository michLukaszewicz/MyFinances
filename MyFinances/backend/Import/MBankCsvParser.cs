using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;

namespace MyFinances.Api.Import;

// Parses the real mBank CSV export shape: cp1250 encoding, ';' delimiter, a variable-length
// preamble before the real header, Polish number/date formats, single-quote-wrapped account
// numbers. See context/changes/mbank-import-with-dedup/plan.md (Phase 2) for the format notes
// this was written against.
public class MBankCsvParser : IBankStatementParser
{
    private const string HeaderPrefix = "#Data księgowania";
    private static readonly Encoding Cp1250 = GetCp1250();
    private static readonly CultureInfo PlPl = CultureInfo.GetCultureInfo("pl-PL");

    // Program.cs registers CodePagesEncodingProvider for the running app, but plain unit tests
    // (this class instantiated directly, no WebApplicationFactory/Program startup) never run
    // that registration. Register defensively here too — safe to call more than once.
    private static Encoding GetCp1250()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1250);
    }

    public string BankName => "mBank";

    public bool CanParse(Stream fileStream)
    {
        var startPosition = fileStream.CanSeek ? fileStream.Position : 0;

        using var reader = new StreamReader(fileStream, Cp1250, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
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
        using var reader = new StreamReader(fileStream, Cp1250, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
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
            if (!DateOnly.TryParseExact(rawDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
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
