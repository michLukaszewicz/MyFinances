namespace MyFinances.Api.Import;

// The file format of an uploaded statement, decided once per upload by StatementFormatSniffer.
public enum StatementFormat
{
    Csv,
    Pdf,
}
