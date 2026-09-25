namespace MyFinances.Api.Categorization;

// Request/response contracts for the categorization queue flow (CategorizationEndpoints).

public record CategoryDto(Guid Id, string Name);

public record TransactionQueueItemDto(
    Guid Id,
    DateOnly Date,
    string Description,
    decimal Amount,
    string BankName,
    string AccountNumber,
    Guid? CategoryId,
    string? CategoryName,
    bool IsInternalTransfer);

// Either field may be omitted/null to leave that aspect unchanged.
public record CategorizeRequest(Guid? CategoryId, bool? IsInternalTransfer);
