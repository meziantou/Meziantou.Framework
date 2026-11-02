namespace Meziantou.Framework.Pdf;

/// <summary>The options of <see cref="PdfDocument.Save(Stream, PdfSaveOptions?)"/>.</summary>
/// <remarks>The output of a save depends only on the document and the options: the same document gives the same bytes.</remarks>
public sealed class PdfSaveOptions
{
    /// <summary>Gets the default options.</summary>
    public static PdfSaveOptions Default { get; } = new();

    /// <summary>Gets a value indicating whether the streams without filter are compressed with <c>FlateDecode</c>. The default is <see langword="true"/>.</summary>
    public bool CompressStreams { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether the objects that are not streams are written in object streams, with a
    /// cross-reference stream (PDF 1.5, ISO 32000-2, 7.5.7 and 7.5.8). The default is <see langword="true"/>. An
    /// incremental save keeps the kind of cross-reference of the original file: it writes the objects of the update in
    /// object streams when the file has cross-reference streams.
    /// </summary>
    public bool UseObjectStreams { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether the file is linearized for fast web view (ISO 32000-2, Annex F): the objects of
    /// the first page come first, with hint tables that let a viewer show each page before the file is fully
    /// downloaded. The default is <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// A linearized file uses cross-reference tables and no object stream (<see cref="UseObjectStreams"/> is ignored),
    /// and is built in memory before it is written. The inheritable attributes of the page tree are written on the
    /// pages. It cannot be encrypted, and an incremental save cannot be linearized: an incremental update of a
    /// linearized file keeps the original file, which is then no longer linearized.
    /// </remarks>
    public bool Linearize { get; init; }

    /// <summary>
    /// Gets the number of whitespace bytes the XMP packet the save writes for <see cref="PdfDocument.PdfAConformance"/>
    /// and <see cref="PdfDocument.PdfUAConformance"/> reserves before its end, so that an editor can update the packet in
    /// place. The default is 0.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public int XmpPadding
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    }

    /// <summary>Gets the version written in the header, or <see langword="null"/> (the default) for the version of the document, raised to what the options need.</summary>
    public PdfVersion? Version { get; init; }
}
