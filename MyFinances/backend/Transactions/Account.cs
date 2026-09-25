namespace MyFinances.Api.Transactions;

// One bank account the user has told the system about (bank name + account number).
// Used as the "known accounts" input to S-03's transfer-detection heuristic and as an
// account picker for manual entry (S-02 follow-up); this slice only stores and CRUDs it.
public class Account
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public required string BankName { get; set; }

    public required string AccountNumber { get; set; }
}
