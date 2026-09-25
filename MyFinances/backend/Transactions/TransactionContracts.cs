namespace MyFinances.Api.Transactions;

// Request/response contracts for the transaction history list (TransactionEndpoints: GET /transactions).

public record TransactionListItemDto(Guid Id, DateOnly Date, string Description, decimal Amount, Guid? CategoryId, string? CategoryName);

public record TransactionListResponseDto(IReadOnlyList<TransactionListItemDto> Items, bool HasMore);
