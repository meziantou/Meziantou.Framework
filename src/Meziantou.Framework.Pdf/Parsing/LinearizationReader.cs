using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Parsing;

/// <summary>
/// Reads the linearization parameter dictionary of a file (ISO 32000-2, Annex F.2.2): the first object of a linearized
/// file, which gives its length (<c>/L</c>) and the end of the first page (<c>/E</c>), so that the first page is
/// downloaded in one request.
/// </summary>
internal static class LinearizationReader
{
    private static readonly PdfName LinearizedName = PdfName.Create("Linearized");
    private static readonly PdfName EName = PdfName.Create("E");
    private static readonly PdfName OName = PdfName.Create("O");

    /// <summary>Reads the linearization dictionary in the first bytes of a file; <see langword="null"/> when the file is not linearized, or its length is not the length of the file (it was updated).</summary>
    public static (long EndOfFirstPage, int FirstPageObjectNumber, int PageCount)? TryRead(ReadOnlyMemory<byte> start, long fileLength)
    {
        var header = start.Span.IndexOf("%PDF-"u8);
        if (header < 0)
            return null;

        // The dictionary is the first object after the header and the comment that follows it (F.3.2)
        var objectStart = start.Span[header..].IndexOf("obj"u8);
        if (objectStart < 0)
            return null;

        var lineStart = start.Span[..(header + objectStart)].LastIndexOfAny((byte)'\n', (byte)'\r') + 1;
        try
        {
            var parser = new PdfObjectParser(new PdfLexer(start, lineStart), objects: null, maxDepth: 8);
            if (parser.ParseIndirectObject(out _, out _) is not PdfDictionary dictionary || !dictionary.ContainsKey(LinearizedName))
                return null;

            var length = dictionary.GetInteger(PdfNames.L, -1);
            var end = dictionary.GetInteger(EName, -1);
            var firstPage = dictionary.GetInteger(OName, -1);
            var count = dictionary.GetInteger(PdfNames.N, -1);
            if (length != fileLength || end <= 0 || end > fileLength || firstPage is <= 0 or > int.MaxValue || count is <= 0 or > int.MaxValue)
                return null;

            return (end, (int)firstPage, (int)count);
        }
        catch (PdfException)
        {
            // The probe is optional: an object it cannot read (too deep for its own limit) is read by the reader with the
            // limits of the options
            return null;
        }
    }
}
