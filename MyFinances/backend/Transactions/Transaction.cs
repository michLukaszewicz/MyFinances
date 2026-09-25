using MyFinances.Api.Categorization;

namespace MyFinances.Api.Transactions;

// One imported (or, in a later slice, manually entered) bank transaction.
// Hash is intentionally not a unique constraint: a colliding hash is an expected,
// legitimate outcome once the user explicitly chooses to keep a duplicate during import.
public class Transaction
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid AccountId { get; set; }

    public Account Account { get; set; } = null!;

    public DateOnly Date { get; set; }

    public required string Description { get; set; }

    public decimal Amount { get; set; }

    public required string Hash { get; set; }

    // Nullable: a later slice will support manual entries not tied to any import.
    public Guid? ImportBatchId { get; set; }

    public ImportBatch? ImportBatch { get; set; }

    // Nullable: uncategorized until the user assigns one via the categorization queue (S-03).
    public Guid? CategoryId { get; set; }

    public Category? Category { get; set; }

    // Auto-flagged by TransferDetectionService, or set explicitly via the categorization
    // queue's PUT endpoint (FR-009).
    public bool IsInternalTransfer { get; set; }

    // Once true, TransferDetectionService permanently skips this row so a user's manual
    // decision is never silently re-flagged by a later automatic detection pass.
    public bool TransferFlagManuallySet { get; set; }
}
