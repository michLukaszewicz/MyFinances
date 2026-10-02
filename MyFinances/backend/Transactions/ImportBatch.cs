using MyFinances.Api.Import;

namespace MyFinances.Api.Transactions;

// One row per completed CSV import, for future auditability (imported/skipped counts).
public class ImportBatch
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid AccountId { get; set; }

    public Account Account { get; set; } = null!;

    public DateTime ImportedAtUtc { get; set; }

    public int ImportedCount { get; set; }

    public int SkippedDuplicateCount { get; set; }

    public int SkippedErrorCount { get; set; }

    // The format the batch was imported from; drives the mixed-format overlap warning on parse.
    public StatementFormat SourceFormat { get; set; } = StatementFormat.Csv;
}
