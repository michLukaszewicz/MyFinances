namespace MyFinances.Api.Import;

// ADAPTATION: Parse returns ParseResult (transactions + skipped/errored row count) instead of
// a bare IReadOnlyList<NormalizedTransaction>. The plan's contract didn't yet account for
// surfacing the skipped-error count that Phase 3's /import/parse endpoint needs to build
// skippedErrorCount in its response — this is the plan's own pre-approved refinement route.
public record ParseResult(IReadOnlyList<NormalizedTransaction> Transactions, int SkippedErrorCount);

// Isolates all bank-specific quirks behind one interface so future bank parsers only need a
// new implementation, never changes to the dedup/endpoint code that consumes parsers generically.
public interface IBankStatementParser
{
    string BankName { get; }

    // Scans the stream for this bank's recognition anchor without consuming it in a way that
    // breaks a subsequent Parse call. If the stream is seekable, implementations reset the
    // position before returning; if it isn't, callers must supply a fresh stream per call.
    bool CanParse(Stream fileStream);

    ParseResult Parse(Stream fileStream);
}
