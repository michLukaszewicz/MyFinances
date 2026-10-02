namespace MyFinances.Api.Transactions;

// The bank dropdown for an account. Names of banks that have a statement parser must match
// that parser's BankName exactly (the import bank-mismatch check compares them); a test pins it.
public static class PolishBanks
{
    // Choosing this means "no check": the import never compares such an account with the
    // detected bank. Also the value existing accounts fall back to when nothing matches.
    public const string Other = "Other";

    // Banks operating in Poland (commercial banks, foreign-bank branches and the two big
    // cooperative networks), alphabetical. Compiled 2026-10-02 from porownywarkabankow.pl/banki
    // and informacjakredytowa.com/banki; edit here when the market changes.
    private static readonly string[] Banks =
    [
        "Aion Bank",
        "Alior Bank",
        "Bank BPH",
        "Bank Handlowy (Citi Handlowy)",
        "Bank Millennium",
        "Bank Nowy",
        "Bank Ochrony Środowiska (BOŚ)",
        "Bank Pekao",
        "Bank Pocztowy",
        "Bank Polskiej Spółdzielczości (BPS)",
        "BNP Paribas",
        "Credit Agricole",
        "Deutsche Bank Polska",
        "DNB Bank Polska",
        "Erste",
        "Ikano Bank",
        "Inbank",
        "ING Bank Śląski",
        "mBank",
        "Mercedes-Benz Bank Polska",
        "Nest Bank",
        "PKO Bank Polski",
        "Plus Bank",
        "Santander Bank Polska",
        "Santander Consumer Bank",
        "SGB-Bank",
        "Toyota Bank Polska",
        "VeloBank",
    ];

    // The dropdown contents: every bank, then Other.
    public static IReadOnlyList<string> Options { get; } = [.. Banks, Other];

    // Canonical spelling of a dropdown value (case-insensitive), or null if it is not one.
    public static string? Canonicalize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : Options.FirstOrDefault(o => string.Equals(o, value.Trim(), StringComparison.OrdinalIgnoreCase));
}
