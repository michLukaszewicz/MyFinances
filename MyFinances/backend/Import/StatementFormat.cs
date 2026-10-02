using System.Text.Json.Serialization;

namespace MyFinances.Api.Import;

// The file format of an uploaded statement, decided once per upload by StatementFormatSniffer.
// String-serialized on the wire ("Csv" | "Pdf"), like RowDecision, since it round-trips from the
// parse response into the commit request.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StatementFormat
{
    Csv,
    Pdf,
}
