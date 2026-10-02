namespace MyFinances.Api.Transactions;

// One bank account the user has told the system about (bank name + account number).
// Used as the "known accounts" input to S-03's transfer-detection heuristic and as an
// account picker for manual entry (S-02 follow-up); this slice only stores and CRUDs it.
public class Account
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    // The user's own free-text label for the account.
    public required string BankName { get; set; }

    // The bank, picked from PolishBanks.Options. Only this is compared with the bank detected
    // from an uploaded statement; PolishBanks.Other means "never check".
    public string Bank { get; set; } = PolishBanks.Other;

    public required string AccountNumber { get; set; }
}
