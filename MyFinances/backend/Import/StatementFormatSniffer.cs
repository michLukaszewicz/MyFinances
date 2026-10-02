namespace MyFinances.Api.Import;

// Decides an upload's format once, from its first bytes, so the endpoint only asks parsers of that
// format whether they recognise the file. Anything that is not a PDF is treated as CSV, including
// empty and very short streams, so the existing "could not recognize" handling stays the single
// fallback for unreadable content.
public static class StatementFormatSniffer
{
    private static readonly byte[] PdfMagic = "%PDF-"u8.ToArray();

    // Restores the position of a seekable stream; a non-seekable stream has its sniffed bytes
    // consumed, so callers must pass a seekable one (the endpoint passes a MemoryStream).
    public static StatementFormat Detect(Stream stream)
    {
        var startPosition = stream.CanSeek ? stream.Position : 0;

        var header = new byte[PdfMagic.Length];
        var read = 0;
        while (read < header.Length)
        {
            var n = stream.Read(header, read, header.Length - read);
            if (n == 0)
            {
                break;
            }

            read += n;
        }

        if (stream.CanSeek)
        {
            stream.Position = startPosition;
        }

        return read == PdfMagic.Length && header.AsSpan().SequenceEqual(PdfMagic)
            ? StatementFormat.Pdf
            : StatementFormat.Csv;
    }
}
