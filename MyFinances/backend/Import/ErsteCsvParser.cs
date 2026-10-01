using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;

namespace MyFinances.Api.Import;

// Parses the Erste Bank Polska CSV export: UTF-8, no header row, every line ends with a trailing
// delimiter, and line 1 is a statement summary rather than a transaction. The export comes in
// four delimiter variants (';', ',', tab, '|'), so there is no fixed delimiter to anchor on —
// recognition and parsing both go through DetectDelimiter. See
// context/changes/erste-import/plan.md (Phase 1) for the format notes this was written against.
public class ErsteCsvParser : IBankStatementParser
{
    private static readonly string[] CandidateDelimiters = [";", ",", "\t", "|"];
    private static readonly CultureInfo PlPl = CultureInfo.GetCultureInfo("pl-PL");

    private const int MinSummaryFieldCount = 8;
    private const int SummaryCurrencyField = 4;

    public string BankName => "Erste";

    // Lenient on purpose: every registered parser's CanParse runs on every upload, including
    // cp1250 mBank files, so invalid UTF-8 must degrade to replacement characters, never throw.
    private static string ReadAllText(Stream fileStream)
    {
        using var buffer = new MemoryStream();
        fileStream.CopyTo(buffer);
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false)
            .GetString(buffer.ToArray())
            .TrimStart('﻿');
    }

    private static CsvConfiguration CreateConfig(string delimiter) => new(CultureInfo.InvariantCulture)
    {
        Delimiter = delimiter,
        HasHeaderRecord = false,
        MissingFieldFound = null,
        BadDataFound = null,
    };

    // Returns the delimiter under which the first non-empty line looks like an Erste summary line
    // (export date, period start, account number starting with a single quote, ..., currency), or
    // null when none fits. Splits with CsvHelper rather than counting characters because ';'
    // files contain decimal commas and the ',' variant double-quotes its amounts.
    private static string? DetectDelimiter(string text)
    {
        using var reader = new StringReader(text);
        string? firstLine;
        while ((firstLine = reader.ReadLine()) is not null && string.IsNullOrWhiteSpace(firstLine))
        {
        }

        if (firstLine is null)
        {
            return null;
        }

        foreach (var delimiter in CandidateDelimiters)
        {
            using var lineReader = new StringReader(firstLine);
            using var csv = new CsvReader(lineReader, CreateConfig(delimiter));
            if (csv.Read() && csv.Parser.Count >= MinSummaryFieldCount && LooksLikeSummary(csv))
            {
                return delimiter;
            }
        }

        return null;
    }

    private static bool LooksLikeSummary(CsvReader csv)
    {
        var currency = csv.GetField(SummaryCurrencyField) ?? string.Empty;
        return DateOnly.TryParseExact(csv.GetField(0), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
            && DateOnly.TryParseExact(csv.GetField(1), "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
            && (csv.GetField(2) ?? string.Empty).StartsWith('\'')
            && currency.Length == 3
            && currency.All(char.IsLetter);
    }

    public bool CanParse(Stream fileStream)
    {
        var startPosition = fileStream.CanSeek ? fileStream.Position : 0;

        var found = DetectDelimiter(ReadAllText(fileStream)) is not null;

        if (fileStream.CanSeek)
        {
            fileStream.Position = startPosition;
        }

        return found;
    }

    public ParseResult Parse(Stream fileStream)
    {
        var text = ReadAllText(fileStream);
        var transactions = new List<NormalizedTransaction>();

        // The endpoint calls Parse on a manually chosen bank even when CanParse rejected the file.
        var delimiter = DetectDelimiter(text);
        if (delimiter is null)
        {
            return new ParseResult(transactions, 0);
        }

        using var reader = new StringReader(text);
        using var csv = new CsvReader(reader, CreateConfig(delimiter));

        // Line 1 is the statement summary (CsvHelper skips leading blank lines itself); it carries
        // the account currency but is never a transaction.
        csv.Read();
        var isPln = string.Equals(csv.GetField(SummaryCurrencyField), "PLN", StringComparison.OrdinalIgnoreCase);

        var skippedErrorCount = 0;
        while (csv.Read())
        {
            if (IsBlankRecord(csv))
            {
                continue;
            }

            if (!isPln)
            {
                // Only PLN is supported; every data row of a foreign-currency statement counts as skipped.
                skippedErrorCount++;
                continue;
            }

            if (!DateOnly.TryParseExact(csv.GetField(1), "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                || !decimal.TryParse(csv.GetField(5), NumberStyles.Number | NumberStyles.AllowLeadingSign, PlPl, out var amount))
            {
                // Row-level parse failure: skip it and keep going, don't abort the rest of the file.
                skippedErrorCount++;
                continue;
            }

            transactions.Add(new NormalizedTransaction(date, csv.GetField(2) ?? string.Empty, amount));
        }

        return new ParseResult(transactions, skippedErrorCount);
    }

    private static bool IsBlankRecord(CsvReader csv)
    {
        for (var i = 0; i < csv.Parser.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(csv.GetField(i)))
            {
                return false;
            }
        }

        return true;
    }
}
