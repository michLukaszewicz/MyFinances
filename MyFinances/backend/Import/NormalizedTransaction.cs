namespace MyFinances.Api.Import;

// Bank-agnostic shape a parser produces. No bank-specific fields leak through here —
// dedup/endpoint code downstream consumes parsers generically via this record.
public record NormalizedTransaction(DateOnly Date, string Description, decimal Amount);
