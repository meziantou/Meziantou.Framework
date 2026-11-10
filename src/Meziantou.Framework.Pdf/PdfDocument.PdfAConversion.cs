using Meziantou.Framework.Pdf.Conformance;

namespace Meziantou.Framework.Pdf;

public sealed partial class PdfDocument
{
    /// <summary>
    /// Changes an existing document so that it can be saved as PDF/A-2 or PDF/A-3 (levels b and u): removes the
    /// encryption, the JavaScript and the actions PDF/A does not allow, generates the missing appearances of the
    /// annotations and the fields, embeds substitute fonts for the standard fonts that are not embedded, re-encodes the
    /// LZW streams, rebuilds the XMP metadata, and completes the embedded files and the color spaces. Nothing that the
    /// page shows moves.
    /// </summary>
    /// <param name="conformance">The level: <see cref="PdfAConformance.PdfA2B"/>, <see cref="PdfAConformance.PdfA2U"/>, <see cref="PdfAConformance.PdfA3B"/> or <see cref="PdfAConformance.PdfA3U"/>.</param>
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
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="conformance"/> is not a level of PDF/A-2 or PDF/A-3 b or u.</exception>
    public PdfAConversionResult ConvertToPdfA(PdfAConformance conformance, PdfAConversionOptions? options = null)
    {
        if (conformance is not (PdfAConformance.PdfA2B or PdfAConformance.PdfA2U or PdfAConformance.PdfA3B or PdfAConformance.PdfA3U))
            throw new ArgumentOutOfRangeException(nameof(conformance), conformance, "The conversion supports PDF/A-2b, PDF/A-2u, PDF/A-3b and PDF/A-3u");

        return ConformanceWriter.ConvertToPdfA(this, conformance, options ?? PdfAConversionOptions.Default);
    }
}
