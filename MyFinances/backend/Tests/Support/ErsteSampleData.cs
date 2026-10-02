namespace MyFinances.Api.Tests.Support;

// Invented Erste statement rows shared by the fixture generator and the parser tests. Every name,
// account number, card number and merchant is made up.
//
// Rows are printed newest booking first, like the real export, but the (unprinted) booking date
// has a booking sequence inside each day, and rows of the same day are printed in an order that
// does NOT always follow it. The printed balances come from one exact chain over the booking order
// (earlier booking + amount == later balance), so the chain only holds order-independently:
// the multiset of "balance before" values equals the multiset of "balance after" values apart
// from the oldest row's before-balance and the newest row's after-balance.
public static class ErsteSampleData
{
    public const int OnePageRowCount = 12;
    public const int MultiPageRowCount = 40;

    // The balance after the newest booked row (not the top printed row: same-day rows print out of booking order).
    public const decimal NewestBalance = 1098.64m;

    public static ErstePdfHeader Header { get; } = new(
        AccountNumber: "00 0000 0000 0000 0000 0000 0000",
        DocumentDate: new DateOnly(2026, 10, 1));

    // 40 rows, newest first; the first 12 are the one-page statement, and the cut between row 12
    // and row 13 falls between two booking days, so the short export is a prefix of the long one
    // whose own balance chain is still complete.
    public static IReadOnlyList<ErstePdfRow> MultiPageRows { get; }

    public static IReadOnlyList<ErstePdfRow> OnePageRows { get; }

    // The same 40 rows in booking order (oldest first): the order the balance chain follows.
    public static IReadOnlyList<ErstePdfRow> MultiPageRowsInBookingOrder { get; }

    // The balance before the oldest of the 40 rows.
    public static decimal OpeningBalance { get; }

    // 12 rows on a single page.
    public static ErstePdfLayout OnePageLayout { get; } = new() { FirstPageRows = OnePageRowCount };

    // 15 + 15 + 10 rows over three pages.
    public static ErstePdfLayout MultiPageLayout { get; } = new() { FirstPageRows = 15, RowsPerPage = 15 };

    static ErsteSampleData()
    {
        var entries = CreateEntries();
        var chronological = entries
            .Select((entry, printedIndex) => (entry, printedIndex))
            .OrderBy(x => x.entry.Booked)
            .ThenBy(x => x.entry.Seq)
            .ToList();

        OpeningBalance = NewestBalance - entries.Sum(e => e.Amount);

        var balances = new decimal[entries.Count];
        var running = OpeningBalance;
        foreach (var (entry, printedIndex) in chronological)
        {
            running += entry.Amount;
            balances[printedIndex] = running;
        }

        MultiPageRows = entries
            .Select((e, i) => new ErstePdfRow(e.Operated, e.Lines, e.Amount, "PLN", balances[i]))
            .ToList();
        OnePageRows = MultiPageRows.Take(OnePageRowCount).ToList();
        MultiPageRowsInBookingOrder = chronological.Select(x => MultiPageRows[x.printedIndex]).ToList();
    }

    public static byte[] BuildOnePagePdf() => ErstePdfBuilder.Build(Header, OnePageRows, OnePageLayout);

    public static byte[] BuildMultiPagePdf() => ErstePdfBuilder.Build(Header, MultiPageRows, MultiPageLayout);

    // One booked operation. Seq is the booking order within the booking day (1 = booked first).
    private sealed record Entry(DateOnly Booked, int Seq, DateOnly Operated, IReadOnlyList<string> Lines, decimal Amount);

    // Rows in printed order (newest booking day first, same-day rows in the order the bank prints them).
    private static List<Entry> CreateEntries()
    {
        Entry E(int month, int day, int seq, string merchant, decimal amount, int operatedDaysEarlier = 0) =>
            new(D(month, day), seq, D(month, day).AddDays(-operatedDaysEarlier), [merchant], amount);

        return
        [
            // Rows 1-12: the one-page statement.
            E(10, 1, 2, "Kawiarnia Testowa Testowo", -23.40m),
            E(10, 1, 1, "Piekarnia Test Testowo", -12.50m, operatedDaysEarlier: 1),
            E(10, 1, 3, "Sklep Testowy 12 Testowo", -64.30m),
            E(9, 30, 1, "Jan Próbny", -50.00m),
            new(D(9, 30), 3, D(9, 30),
                ["DOP. MC 000000******0000 ZWROT PŁATNOŚCI KARTĄ 29.99 PLN", "Sklep Testowy 12 Testowo"], 29.99m),
            E(9, 30, 2, "Market Przykładowy Testowo", -142.35m),
            // A group of rows sharing date, amount and description.
            E(9, 29, 1, "Sklep Internetowy Testowy", -19.99m),
            E(9, 29, 2, "Sklep Internetowy Testowy", -19.99m),
            E(9, 29, 3, "Sklep Internetowy Testowy", -19.99m),
            E(9, 29, 4, "Biuro Przykładowe Sp. z o.o.", 1200.00m),
            E(9, 28, 2, "Apteka Przykładowa Testowo", -89.99m),
            E(9, 28, 1, "Stacja Paliw 0042 Testowo", -158.10m),

            // Rows 13-40: older rows that only the long statement contains.
            E(9, 27, 2, "Market Przykładowy Testowo", -71.10m),
            E(9, 27, 1, "Kiosk Testowy Testowo", -9.90m),
            E(9, 26, 2, "Piekarnia Test Testowo", -16.75m),
            E(9, 26, 1, "Kawiarnia Testowa Testowo", -48.30m),
            E(9, 25, 2, "Anna Testowa", 300.00m),
            E(9, 25, 1, "Apteka Przykładowa Testowo", -33.45m),
            E(9, 24, 1, "Sklep Testowy 12 Testowo", -94.50m),
            E(9, 24, 2, "Stacja Paliw 0042 Testowo", -61.20m),
            E(9, 23, 2, "Kiosk Testowy Testowo", -11.40m),
            E(9, 23, 1, "Market Przykładowy Testowo", -53.70m),
            E(9, 22, 2, "Kawiarnia Testowa Testowo", -26.00m),
            E(9, 22, 1, "Piekarnia Test Testowo", -7.80m),
            E(9, 21, 2, "Adam Próbny", -420.00m),
            E(9, 21, 1, "Sklep Internetowy Testowy", -39.99m),
            E(9, 20, 2, "Market Przykładowy Testowo", -102.00m),
            E(9, 20, 1, "Stacja Paliw 0042 Testowo", -58.10m),
            E(9, 19, 2, "Anna Testowa", 120.00m),
            E(9, 19, 1, "Kiosk Testowy Testowo", -19.99m),
            E(9, 18, 1, "Apteka Przykładowa Testowo", -44.00m),
            E(9, 18, 2, "Sklep Testowy 12 Testowo", -67.80m),
            E(9, 17, 2, "Kawiarnia Testowa Testowo", -23.40m),
            E(9, 17, 1, "Piekarnia Test Testowo", -12.00m),
            E(9, 16, 2, "Market Przykładowy Testowo", -88.15m),
            E(9, 16, 1, "Sklep Internetowy Testowy", -29.90m),
            E(9, 15, 2, "Stacja Paliw 0042 Testowo", -64.00m),
            E(9, 15, 1, "Kiosk Testowy Testowo", -5.40m),
            E(9, 14, 2, "Sklep Testowy 12 Testowo", -35.80m),
            E(9, 14, 1, "Kawiarnia Testowa Testowo", -18.20m),
        ];
    }

    private static DateOnly D(int month, int day) => new(2026, month, day);
}
