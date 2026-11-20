using Meziantou.Framework.Pdf.Conformance;

namespace Meziantou.Framework.Pdf;

public sealed partial class PdfDocument
{
    /// <summary>
    /// Changes an existing document so that it can be saved as PDF/A-2 or PDF/A-3 (levels b, u, and a for a tagged
    /// document): removes the
    /// encryption, the JavaScript and the actions PDF/A does not allow, generates the missing appearances of the
    /// annotations and the fields, embeds substitute fonts for the standard fonts that are not embedded, re-encodes the
    /// LZW streams, rebuilds the XMP metadata, and completes the embedded files and the color spaces. Nothing that the
    /// page shows moves.
    /// </summary>
    /// <param name="conformance">The level: <see cref="PdfAConformance.PdfA2B"/>, <see cref="PdfAConformance.PdfA2U"/>, <see cref="PdfAConformance.PdfA3B"/>, <see cref="PdfAConformance.PdfA3U"/>, or <see cref="PdfAConformance.PdfA2A"/> and <see cref="PdfAConformance.PdfA3A"/> for a document that has a structure tree (<see cref="AutoTag(PdfAutoTagOptions?)"/> tags one).</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="PdfAConversionOptions.Default"/>.</param>
    /// <returns>
    /// The changes, and the requirements the conversion could not fix. When there are none,
    /// <see cref="PdfAConformance"/> is set to <paramref name="conformance"/>, so that <see cref="Save(Stream, PdfSaveOptions?)"/>
    /// writes a PDF/A document; otherwise it is not changed.
    /// </returns>
    /// <remarks>
    /// The document is changed by the fixes even when some requirements cannot be met: open the file again to start
    /// over. The library checks the requirements it knows (see <see cref="PdfAConformance"/>); validate the documents
    /// with a validator such as veraPDF.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="conformance"/> is not a level of PDF/A-2 or PDF/A-3 b, u or a.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="conformance"/> is a level a, and the document is not tagged.</exception>
    public PdfAConversionResult ConvertToPdfA(PdfAConformance conformance, PdfAConversionOptions? options = null)
    {
        if (conformance is not (PdfAConformance.PdfA2B or PdfAConformance.PdfA2U or PdfAConformance.PdfA3B or PdfAConformance.PdfA3U or PdfAConformance.PdfA2A or PdfAConformance.PdfA3A))
            throw new ArgumentOutOfRangeException(nameof(conformance), conformance, "The conversion supports PDF/A-2 and PDF/A-3, levels b, u and a");

        if (conformance is PdfAConformance.PdfA2A or PdfAConformance.PdfA3A && !StructureTree.Exists)
            throw new InvalidOperationException("The level a of PDF/A needs a tagged document: tag it first (AutoTag)");

        return ConformanceWriter.ConvertToPdfA(this, conformance, options ?? PdfAConversionOptions.Default);
    }
}
