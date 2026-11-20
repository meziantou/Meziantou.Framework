using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Parsing;

/// <summary>A cross-reference section and its trailer: a table (7.5.4) or a stream (7.5.8).</summary>
internal sealed class XrefSection(Dictionary<int, XrefEntry> entries, PdfDictionary trailer, bool isStream)
{
    public Dictionary<int, XrefEntry> Entries { get; } = entries;

    public PdfDictionary Trailer { get; } = trailer;

    public bool IsStream { get; } = isStream;

    /// <summary>Gets a value indicating whether the trailer of the table cannot be read: <see cref="Trailer"/> is empty, and only the entries of the table are kept.</summary>
    public bool IsTrailerDamaged { get; init; }
}
