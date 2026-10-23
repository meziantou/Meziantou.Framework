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
    /// incremental save keeps the kind of cross-reference of the original file and writes no object stream.
    /// </summary>
    public bool UseObjectStreams { get; init; } = true;

    /// <summary>Gets the version written in the header, or <see langword="null"/> (the default) for the version of the document, raised to what the options need.</summary>
    public PdfVersion? Version { get; init; }
}
