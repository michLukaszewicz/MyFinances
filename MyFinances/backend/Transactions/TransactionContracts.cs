using MyFinances.Api.Import;

namespace MyFinances.Api.Transactions;

// Request/response contracts for the transaction history list and manual write endpoints
// (TransactionEndpoints: GET /transactions, POST /transactions, PUT/DELETE /transactions/{id}).

// AccountId and IsInternalTransfer appended as the last positional parameters so the existing single construction site
// (TransactionEndpoints.cs's GET /) doesn't need every other positional argument reordered.
public record TransactionListItemDto(Guid Id, DateOnly Date, string Description, decimal Amount, Guid? CategoryId, string? CategoryName, Guid AccountId, bool IsInternalTransfer);

public record TransactionListResponseDto(IReadOnlyList<TransactionListItemDto> Items, bool HasMore);

// One shape for both POST and PUT. Force has no server-side default because the frontend always
// sends it explicitly.
public record TransactionWriteRequest(DateOnly Date, string Description, decimal Amount, Guid AccountId, Guid CategoryId, bool Force);

public record TransactionDetailDto(Guid Id, DateOnly Date, string Description, decimal Amount, Guid AccountId, Guid CategoryId, string CategoryName);

// The 409 body returned by POST/PUT when a duplicate hash is found and Force is false. Reuses
// ExistingTransactionDto rather than duplicating its shape.
public record DuplicateTransactionResponse(string Title, ExistingTransactionDto ExistingTransaction);
