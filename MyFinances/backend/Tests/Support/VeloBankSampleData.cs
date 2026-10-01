namespace MyFinances.Api.Tests.Support;

// Invented VeloBank statement rows shared by the fixture generator and the parser tests.
// Every name, account number, card number and merchant is made up. The running balances form
// one exact chain over the booked PLN rows (newer.Balance - newer.Amount == older.Balance);
// pending rows print "-" instead of a balance and sit outside the chain.
public static class VeloBankSampleData
{
    public const int OnePageRowCount = 17;
    public const int MultiPageRowCount = 40;

    public static VeloBankPdfHeader Header { get; } = new(
        AccountNumber: "00 0000 0000 0000 0000 0000 0000",
        HolderName: "JAN PRZYKŁADOWY",
        AddressLine: "ul. Testowa 1/2, 00-000 Testowo",
        PeriodLabel: "Ostatnie 90 dni");

    // 40 rows, newest first; the first 17 are the one-page statement, so the short export is a
    // prefix of the long one (as with the real 90-day and one-year exports).
    public static IReadOnlyList<VeloBankPdfRow> MultiPageRows { get; } = CreateRows();

    public static IReadOnlyList<VeloBankPdfRow> OnePageRows { get; } = MultiPageRows.Take(OnePageRowCount).ToList();

    // Page 1 holds all 17 rows and a repeated header block after the 15th, as in the real 90-day sample.
    public static VeloBankPdfLayout OnePageLayout { get; } = new()
    {
        FirstPageRows = OnePageRowCount,
        MidPageHeaderAfterRows = 15,
    };

    // 15 + 19 + 6 rows over three pages.
    public static VeloBankPdfLayout MultiPageLayout { get; } = new();

    public static byte[] BuildOnePagePdf() => VeloBankPdfBuilder.Build(Header, OnePageRows, OnePageLayout);

    public static byte[] BuildMultiPagePdf() => VeloBankPdfBuilder.Build(Header, MultiPageRows, MultiPageLayout);

    // "Operacja kartą 0000 **** **** 0000 na kwotę 12,34 PLN w <merchant>, <location>"; the location
    // moves to a second line after a long merchant name, as in the real files.
    public static IReadOnlyList<string> CardLines(decimal amount, string currency, string merchant, string location = "Testowo, PL")
    {
        var prefix = $"Operacja kartą 0000 **** **** 0000 na kwotę {VeloBankPdfBuilder.FormatMoney(Math.Abs(amount))} {currency} w ";
        return merchant.Length > 16
            ? [prefix + merchant + ",", location]
            : [prefix + merchant + ", " + location];
    }

    public static IReadOnlyList<string> IncomingTransferLines(string account, string sender, params string[] titleLines) =>
        TransferLines($"Przelew z rachunku: {account},", $"Nadawca: {sender},", titleLines);

    public static IReadOnlyList<string> OutgoingTransferLines(string account, string recipient, params string[] titleLines) =>
        TransferLines($"Przelew na rachunek: {account},", $"Odbiorca: {recipient},", titleLines);

    // Gives every booked PLN row its running balance, walking from the newest row down. The newest
    // booked PLN row prints newestBookedBalance; each older one prints newer.Balance - newer.Amount.
    // Pending rows and rows in other currencies keep the Balance they already have.
    public static IReadOnlyList<VeloBankPdfRow> WithRunningBalances(IReadOnlyList<VeloBankPdfRow> rows, decimal newestBookedBalance)
    {
        var result = new List<VeloBankPdfRow>(rows.Count);
        decimal? balance = newestBookedBalance;
        foreach (var row in rows)
        {
            if (row.BookingDate is null || row.Currency != "PLN")
            {
                result.Add(row);
                continue;
            }
            result.Add(row with { Balance = balance });
            balance -= row.Amount;
        }
        return result;
    }

    private static IReadOnlyList<VeloBankPdfRow> CreateRows()
    {
        var other = new[] { "11 1111 1111 1111 1111 1111 1111", "22 2222 2222 2222 2222 2222 2222", "33 3333 3333 3333 3333 3333 3333" };

        VeloBankPdfRow Pending(int day, decimal amount, string merchant) =>
            new(D(9, day), null, CardLines(amount, "PLN", merchant), amount, "PLN", null);
        VeloBankPdfRow Card(int month, int day, int bookedMonth, int bookedDay, decimal amount, string merchant) =>
            new(D(month, day), D(bookedMonth, bookedDay), CardLines(amount, "PLN", merchant), amount, "PLN", null);
        VeloBankPdfRow Transfer(int month, int day, decimal amount, IReadOnlyList<string> lines) =>
            new(D(month, day), D(month, day), lines, amount, "PLN", null);

        var rows = new List<VeloBankPdfRow>
        {
            // Rows 1-17: the one-page statement.
            Pending(30, -23.40m, "KAWIARNIA TESTOWA"),
            Pending(30, -112.05m, "SKLEP TESTOWY 12"),
            Card(9, 29, 9, 30, -8.99m, "PIEKARNIA TEST"),
            Transfer(9, 29, -150.00m, OutgoingTransferLines(other[0], "FIRMA PRZYKŁADOWA SP. Z O.O.", "Faktura FV/2026/09/001")),
            Card(9, 28, 9, 29, -64.30m, "SKLEP TESTOWY 12"),
            Card(9, 28, 9, 28, -19.99m, "KIOSK TESTOWY"),
            Transfer(9, 27, 300.00m, IncomingTransferLines(other[1], "ANNA TESTOWA", "Zwrot za bilety")),
            Card(9, 26, 9, 27, -42.15m, "STACJA PALIW 0042"),
            Card(9, 26, 9, 26, -7.50m, "KAWIARNIA TESTOWA"),
            Card(9, 25, 9, 26, -89.00m, "APTEKA PRZYKŁADOWA"),
            Transfer(9, 24, -420.00m, OutgoingTransferLines(other[2], "ADAM PRÓBNY", "Czynsz za wrzesień 2026")),
            Transfer(9, 23, 1200.00m, IncomingTransferLines(other[0], "BIURO PRZYKŁADOWE SP. Z O.O.",
                "Wynagrodzenie za wrzesień 2026 - rozliczenie",
                "godzin nadliczbowych oraz premii kwartalnej",
                "zgodnie z aneksem nr 1 do umowy testowej")),
            Card(9, 22, 9, 23, -35.80m, "SKLEP TESTOWY 12"),
            Card(9, 21, 9, 22, -12.00m, "PIEKARNIA TEST"),
            Transfer(9, 20, -75.00m, OutgoingTransferLines(other[1], "ANNA TESTOWA", "Prezent urodzinowy")),
            Card(9, 19, 9, 20, -5.40m, "KIOSK TESTOWY"),
            Card(9, 18, 9, 19, -27.60m, "KAWIARNIA TESTOWA"),

            // Rows 18-40: older rows that only the long statement contains.
            Card(9, 17, 9, 18, -58.20m, "SKLEP TESTOWY 12"),
            Card(9, 16, 9, 17, -14.99m, "STACJA PALIW 0042"),
            Transfer(9, 15, -200.00m, OutgoingTransferLines(other[2], "ADAM PRÓBNY", "Zwrot pożyczki")),
            Card(9, 14, 9, 15, -33.45m, "APTEKA PRZYKŁADOWA"),
            Card(9, 13, 9, 14, -9.90m, "PIEKARNIA TEST"),
            Card(9, 12, 9, 13, -71.10m, "SKLEP TESTOWY 12"),
            Transfer(9, 11, 120.00m, IncomingTransferLines(other[1], "ANNA TESTOWA", "Wspólne zakupy")),
            Card(9, 10, 9, 11, -16.75m, "KIOSK TESTOWY"),
            Card(9, 9, 9, 10, -48.00m, "KAWIARNIA TESTOWA"),
            Card(9, 8, 9, 9, -22.30m, "STACJA PALIW 0042"),
            Transfer(9, 7, -310.00m, OutgoingTransferLines(other[0], "FIRMA PRZYKŁADOWA SP. Z O.O.", "Faktura FV/2026/09/000")),
            Card(9, 6, 9, 7, -6.20m, "PIEKARNIA TEST"),
            Card(9, 5, 9, 6, -94.50m, "SKLEP TESTOWY 12"),
            Card(9, 4, 9, 5, -39.99m, "APTEKA PRZYKŁADOWA"),
            Transfer(9, 3, 85.00m, IncomingTransferLines(other[2], "ADAM PRÓBNY", "Rozliczenie")),
            Card(9, 2, 9, 3, -11.40m, "KIOSK TESTOWY"),
            Card(9, 1, 9, 2, -26.00m, "KAWIARNIA TESTOWA"),
            Card(8, 31, 9, 1, -53.70m, "SKLEP TESTOWY 12"),
            Card(8, 30, 8, 31, -18.20m, "STACJA PALIW 0042"),
            Transfer(8, 29, -130.00m, OutgoingTransferLines(other[1], "ANNA TESTOWA", "Bilety do kina")),
            Card(8, 28, 8, 29, -44.00m, "PIEKARNIA TEST"),
            Card(8, 27, 8, 28, -67.80m, "SKLEP TESTOWY 12"),
            Card(8, 26, 8, 27, -9.99m, "KIOSK TESTOWY"),
        };

        // 1 024,50 PLN is the balance after the newest booked row.
        return WithRunningBalances(rows, 1024.50m);
    }

    private static IReadOnlyList<string> TransferLines(string accountLine, string partyLine, string[] titleLines)
    {
        var lines = new List<string> { accountLine, partyLine, "Tytuł: " + titleLines[0] };
        lines.AddRange(titleLines.Skip(1));
        return lines;
    }

    private static DateOnly D(int month, int day) => new(2026, month, day);
}
