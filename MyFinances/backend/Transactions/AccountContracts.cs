namespace MyFinances.Api.Transactions;

// Request/response contracts for the account CRUD flow (AccountEndpoints).

public record AccountDto(Guid Id, string BankName, string AccountNumber);

public record AccountWriteRequest(string BankName, string AccountNumber);

public record BankOptionsResponse(IReadOnlyList<string> BankNames);
