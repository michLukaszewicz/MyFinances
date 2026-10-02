using System.Text.Json.Serialization;

namespace MyFinances.Api.Import;

// Request/response contracts for the import parse -> review -> commit flow
// (ImportEndpoints: POST /import/parse, POST /import/commit).

public record ExistingTransactionDto(DateOnly Date, string Description, decimal Amount);

public record ImportParseRow(DateOnly Date, string Description, decimal Amount, bool IsDuplicate, ExistingTransactionDto? ExistingTransaction);

// BankMismatch: true when the detected/selected parser's BankName differs from the chosen
// account's Bank (never true when that is "Other"). Non-blocking — the caller decides whether to proceed anyway.
// Format: the parser's statement format, echoed back by the client on commit (SourceFormat).
// MixedFormatOverlapCount: the user's transactions on this account imported from the other format
// within the parsed rows' date range — a non-blocking warning, since their hashes cannot collide.
public record ImportParseResponse(string Bank, bool BankMismatch, IReadOnlyList<ImportParseRow> Rows, int SkippedErrorCount, StatementFormat Format, int MixedFormatOverlapCount);

// String-serialized (not the S.T.Json default of numeric) so the wire contract matches the
// plan's own notation (Decision: Keep|Skip) and stays self-describing for the frontend.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RowDecision
{
    Keep,
    Skip,
}

public record ImportCommitRow(DateOnly Date, string Description, decimal Amount, RowDecision Decision);

// SourceFormat is optional for older clients (a missing value binds to Csv, the enum's default).
public record ImportCommitRequest(Guid AccountId, int SkippedErrorCount, IReadOnlyList<ImportCommitRow> Rows, StatementFormat SourceFormat = StatementFormat.Csv);

public record ImportSummaryDto(Guid ImportBatchId, Guid AccountId, DateTime ImportedAtUtc, int ImportedCount, int SkippedDuplicateCount, int SkippedErrorCount);
