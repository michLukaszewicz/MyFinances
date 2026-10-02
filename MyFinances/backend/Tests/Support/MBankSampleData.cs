namespace MyFinances.Api.Tests.Support;

// Invented mBank statement rows shared by the fixture generator and the parser tests. Every name,
// account number, card number and merchant is made up. Rows run oldest first and the running
// balances form one exact chain from OpeningBalance (previous balance + amount == balance).
public static class MBankSampleData
{
    public const int TwoPageRowCount = 20;
    public const int MultiPageRowCount = 40;

    public const decimal OpeningBalance = 2100.00m;

    public static MBankPdfHeader Header { get; } = new(
        HolderName: "JAN PRZYKŁADOWY",
        AccountNumber: "00 0000 0000 0000 0000 0000 0000",
        PeriodFrom: new DateOnly(2026, 9, 10),
        PeriodTo: new DateOnly(2026, 10, 1),
        Currency: "PLN");

    // 40 rows, oldest first; the first 20 are the two-page statement, so the short export is a
    // prefix of the long one.
    public static IReadOnlyList<MBankPdfRow> MultiPageRows { get; } = CreateRows();

    public static IReadOnlyList<MBankPdfRow> TwoPageRows { get; } = MultiPageRows.Take(TwoPageRowCount).ToList();

    // 12 rows on page 1 (every one a two-line row, as in the real export), the other 8 on page 2.
    public static MBankPdfLayout TwoPageLayout { get; } = new() { FirstPageRows = 12 };

    // 12 + 14 + 14 rows over three pages.
    public static MBankPdfLayout MultiPageLayout { get; } = new() { FirstPageRows = 12, RowsPerPage = 14 };

    public static byte[] BuildTwoPagePdf() => MBankPdfBuilder.Build(Header, OpeningBalance, TwoPageRows, TwoPageLayout);

    public static byte[] BuildMultiPagePdf() => MBankPdfBuilder.Build(Header, OpeningBalance, MultiPageRows, MultiPageLayout);

    public static IReadOnlyList<string> CardLines(DateOnly transactionDate, string merchant) =>
        ["ZAKUP PRZY UŻYCIU KARTY", $"{merchant} /Testowo DATA TRANSAKCJI: {MBankPdfBuilder.FormatDate(transactionDate)}"];

    public static IReadOnlyList<string> RefundLines(DateOnly transactionDate, string merchant) =>
        ["POS ZWROT TOWARU", $"{merchant} /Testowo DATA TRANSAKCJI: {MBankPdfBuilder.FormatDate(transactionDate)}"];

    public static IReadOnlyList<string> BlikOutgoingLines(string person) => ["BLIK P2P-WYCHODZĄCY", person];

    public static IReadOnlyList<string> BlikIncomingLines(string person) => ["BLIK P2P-PRZYCHODZĄCY", person];

    // Name, address, account number and title on separate lines, as in the real incoming transfers.
    public static IReadOnlyList<string> IncomingTransferLines(string sender, string address, string account, string title) =>
        ["PRZELEW ZEWNĘTRZNY PRZYCHODZĄCY", sender, address, account, title];

    public static IReadOnlyList<string> OutgoingTransferLines(string recipient, string account, string title) =>
        ["PRZELEW ZEWNĘTRZNY WYCHODZĄCY", recipient, account, title];

    // Gives every row its running balance, walking from the opening balance in the order given.
    public static IReadOnlyList<MBankPdfRow> WithRunningBalances(decimal openingBalance, IReadOnlyList<MBankPdfRow> rows)
    {
        var result = new List<MBankPdfRow>(rows.Count);
        var balance = openingBalance;
        foreach (var row in rows)
        {
            balance += row.Amount;
            result.Add(row with { Balance = balance });
        }
        return result;
    }

    private static IReadOnlyList<MBankPdfRow> CreateRows()
    {
        var other = new[] { "11 1111 1111 1111 1111 1111 1111", "22 2222 2222 2222 2222 2222 2222", "33 3333 3333 3333 3333 3333 3333" };

        // Booked and operated on the same day unless an earlier operation date is given.
        MBankPdfRow Row(int month, int day, decimal amount, Func<DateOnly, IReadOnlyList<string>> lines, int operatedDaysEarlier = 0)
        {
            var booked = D(month, day);
            var operated = booked.AddDays(-operatedDaysEarlier);
            return new MBankPdfRow(booked, operated, lines(operated), amount, 0m);
        }
        Func<DateOnly, IReadOnlyList<string>> Card(string merchant) => d => CardLines(d, merchant);
        Func<DateOnly, IReadOnlyList<string>> Refund(string merchant) => d => RefundLines(d, merchant);
        Func<DateOnly, IReadOnlyList<string>> Fixed(IReadOnlyList<string> lines) => _ => lines;

        var rows = new List<MBankPdfRow>
        {
            // Rows 1-20: the two-page statement.
            Row(9, 10, -34.20m, Card("SKLEP TESTOWY 12")),
            Row(9, 10, -12.50m, Card("PIEKARNIA TEST")),
            Row(9, 11, -50.00m, Fixed(BlikOutgoingLines("ANNA TESTOWA"))),
            Row(9, 12, -89.99m, Card("APTEKA PRZYKŁADOWA"), operatedDaysEarlier: 1),
            Row(9, 12, 25.00m, Refund("SKLEP TESTOWY 12")),
            Row(9, 13, -7.80m, Card("KIOSK TESTOWY")),
            Row(9, 14, -142.35m, Card("MARKET PRZYKŁADOWY")),
            Row(9, 14, -23.40m, Card("KAWIARNIA TESTOWA")),
            Row(9, 15, 60.00m, Fixed(BlikIncomingLines("ANNA TESTOWA"))),
            Row(9, 15, -58.10m, Card("STACJA PALIW 0042")),
            Row(9, 16, -19.99m, Card("KIOSK TESTOWY")),
            Row(9, 16, -75.60m, Card("SKLEP TESTOWY 12")),
            Row(9, 17, 3200.00m, Fixed(IncomingTransferLines(
                "BIURO PRZYKŁADOWE SP. Z O.O.", "UL. TESTOWA 1 00-000 TESTOWO", other[0], "WYNAGRODZENIE ZA WRZESIEŃ 2026"))),
            Row(9, 17, -1200.00m, Fixed(OutgoingTransferLines("ADAM PRÓBNY", other[2], "CZYNSZ ZA WRZESIEŃ 2026"))),
            Row(9, 18, -30.00m, Fixed(BlikOutgoingLines("JAN PRÓBNY"))),
            Row(9, 19, -310.00m, Fixed(OutgoingTransferLines("FIRMA PRZYKŁADOWA SP. Z O.O.", other[0], "FAKTURA FV/2026/09/001"))),
            Row(9, 20, -16.75m, Card("PIEKARNIA TEST")),
            Row(9, 21, -48.30m, Card("KAWIARNIA TESTOWA")),
            Row(9, 22, -102.00m, Card("MARKET PRZYKŁADOWY")),
            Row(9, 22, 120.00m, Fixed(IncomingTransferLines(
                "ANNA TESTOWA", "UL. PRÓBNA 5/7 00-001 TESTOWO", other[1], "WSPÓLNE ZAKUPY"))),

            // Rows 21-40: later rows that only the three-page statement contains.
            Row(9, 23, -27.60m, Card("KAWIARNIA TESTOWA")),
            Row(9, 23, -64.99m, Card("SKLEP TESTOWY 12")),
            Row(9, 24, -15.00m, Fixed(BlikOutgoingLines("ANNA TESTOWA"))),
            Row(9, 24, -38.45m, Card("STACJA PALIW 0042"), operatedDaysEarlier: 1),
            Row(9, 25, 18.99m, Refund("KIOSK TESTOWY")),
            Row(9, 25, -9.90m, Card("PIEKARNIA TEST")),
            Row(9, 26, -71.10m, Card("MARKET PRZYKŁADOWY")),
            Row(9, 26, -420.00m, Fixed(OutgoingTransferLines("ADAM PRÓBNY", other[2], "ZWROT POŻYCZKI"))),
            Row(9, 27, -33.45m, Card("APTEKA PRZYKŁADOWA")),
            Row(9, 28, 85.00m, Fixed(IncomingTransferLines(
                "ADAM PRÓBNY", "UL. PRÓBNA 9 00-002 TESTOWO", other[2], "ROZLICZENIE"))),
            Row(9, 28, -11.40m, Card("KIOSK TESTOWY")),
            Row(9, 29, -94.50m, Card("SKLEP TESTOWY 12")),
            Row(9, 29, -26.00m, Card("KAWIARNIA TESTOWA")),
            Row(9, 30, -53.70m, Card("MARKET PRZYKŁADOWY")),
            Row(9, 30, 40.00m, Fixed(BlikIncomingLines("JAN PRÓBNY"))),
            Row(10, 1, -18.20m, Card("STACJA PALIW 0042")),
            Row(10, 1, -44.00m, Card("PIEKARNIA TEST")),
            Row(10, 1, -130.00m, Fixed(OutgoingTransferLines("ANNA TESTOWA", other[1], "BILETY DO KINA"))),
            Row(10, 1, -67.80m, Card("SKLEP TESTOWY 12")),
            Row(10, 1, -9.99m, Card("KIOSK TESTOWY")),
        };

        return WithRunningBalances(OpeningBalance, rows);
    }

    private static DateOnly D(int month, int day) => new(2026, month, day);
}
