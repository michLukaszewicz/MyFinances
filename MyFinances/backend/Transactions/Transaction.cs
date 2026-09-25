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

    // Nullable: categorization lands in a later slice.
    public Guid? CategoryId { get; set; }
}
