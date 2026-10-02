using System.Globalization;
using System.Text;

namespace MyFinances.Api.Tests.Support;

// One row of an invented mBank CSV export: booking date, the "Tytuł" text the parser reads as the
// description, and the amount. Every other column is filler.
public sealed record MBankCsvRow(DateOnly BookingDate, string Title, decimal Amount);

// Builds an mBank CSV export (cp1250, ';' delimiter, preamble before the real header, Polish decimal
// commas) from independent row records, so tests state their expected rows themselves instead of
// reading them back from a parser run. Layout follows Fixtures/mbank-sample-redacted.csv.
public static class MBankCsvBuilder
{
    public static byte[] Build(IReadOnlyList<MBankCsvRow> rows)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var pl = CultureInfo.GetCultureInfo("pl-PL");

        var text = new StringBuilder();
        text.AppendLine("mBank S.A.;;;;;;;;");
        text.AppendLine("Elektroniczne zestawienie operacji;;;;;;;;");
        text.AppendLine("Klient: Jan Testowy;;;;;;;;");
        text.AppendLine("Numer rachunku: '00000000000000000000000000';;;;;;;;");
        text.AppendLine("Waluta: PLN;;;;;;;;");
        text.AppendLine("#Data księgowania;#Data operacji;#Opis operacji;#Tytuł;#Nadawca/Odbiorca;#Numer konta;#Kwota;#Saldo po operacji;");

        foreach (var row in rows)
        {
            var date = row.BookingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var amount = row.Amount.ToString("F2", pl);
            text.AppendLine($"{date};{date};TEST OPERATION;\"{row.Title}\";\"\";'';{amount};0,00;");
        }

        text.AppendLine();
        text.AppendLine("#Saldo końcowe;0,00 PLN;;;;;;;");

        return Encoding.GetEncoding(1250).GetBytes(text.ToString());
    }
}
