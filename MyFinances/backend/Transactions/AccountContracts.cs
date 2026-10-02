namespace MyFinances.Api.Transactions;

// Request/response contracts for the account CRUD flow (AccountEndpoints).
// BankName is the user's own label; Bank is the dropdown value (PolishBanks.Options).

public record AccountDto(Guid Id, string BankName, string AccountNumber, string Bank);

// Bank omitted: derived from BankName when that happens to be a known bank, else "Other".
public record AccountWriteRequest(string BankName, string AccountNumber, string? Bank = null);

public record BankOptionsResponse(IReadOnlyList<string> BankNames);
