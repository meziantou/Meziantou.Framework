using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Parsing;

/// <summary>A cross-reference section and its trailer: a table (7.5.4) or a stream (7.5.8).</summary>
internal sealed class XrefSection(Dictionary<int, XrefEntry> entries, PdfDictionary trailer, bool isStream)
{
    public Dictionary<int, XrefEntry> Entries { get; } = entries;

    public PdfDictionary Trailer { get; } = trailer;

    public bool IsStream { get; } = isStream;
}
