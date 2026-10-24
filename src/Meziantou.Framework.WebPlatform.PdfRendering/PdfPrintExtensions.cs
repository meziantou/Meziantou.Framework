using Meziantou.Framework.Pdf;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.PdfRendering;

/// <summary>
/// Prints the documents of a browsing context into PDF documents (https://drafts.csswg.org/css-page-3/): the document is
/// laid out with the <c>print</c> media type and paginated (the page size, margins and page names of the <c>@page</c> rules,
/// the page breaks), and each page is a page of the PDF document, drawn with vector graphics and text.
/// </summary>
/// <remarks>
/// <para>
/// The text is shown with the font files the layout used, embedded as subsets, at the positions the layout computed: it
/// can be selected and extracted. The images are embedded (the JPEG images as they are). What PDF cannot draw (blurred
/// shadows, filters, some compositing operators, 3D transforms, conic gradients, color glyphs) is drawn as images.
/// </para>
/// <para>
/// The printing does not change the document. As for the screenshots, call it from the thread of the <see cref="DomEventLoop"/>
/// of the browsing context, after the resources are loaded. The images are only embedded when the browsing context has a
/// <see cref="BrowsingContextOptions.RenderingBackend"/> (which keeps their bytes), such as the <c>ImagingRenderingBackend</c>
/// of Meziantou.Framework.WebPlatform.Imaging.
/// </para>
/// </remarks>
public static class PdfPrintExtensions
{
    /// <summary>Prints the active document of a browsing context into a PDF document.</summary>
    /// <param name="browsingContext">The browsing context.</param>
    /// <param name="printOptions">The page size and the default margins, or null for <see cref="PrintOptions.Default"/>.</param>
    /// <param name="options">The options of the PDF document, or null for <see cref="PdfPrintOptions.Default"/>.</param>
    /// <returns>The PDF document, with one page per printed page. Save it with <see cref="PdfDocument.Save(Stream, PdfSaveOptions?)"/>.</returns>
    /// <exception cref="InvalidOperationException">The document is not rendered, or it is already being printed.</exception>
    public static PdfDocument PrintToPdf(this BrowsingContext browsingContext, PrintOptions? printOptions = null, PdfPrintOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(browsingContext);
        return PrintToPdf(browsingContext.Document, printOptions, options);
    }

    /// <summary>Prints a document into a PDF document.</summary>
    /// <param name="document">The document, which must be the active document of a browsing context.</param>
    /// <param name="printOptions">The page size and the default margins, or null for <see cref="PrintOptions.Default"/>.</param>
    /// <param name="options">The options of the PDF document, or null for <see cref="PdfPrintOptions.Default"/>.</param>
    /// <returns>The PDF document, with one page per printed page. Save it with <see cref="PdfDocument.Save(Stream, PdfSaveOptions?)"/>.</returns>
    /// <exception cref="InvalidOperationException">The document is not rendered (it has no browsing context), or it is already being printed.</exception>
    public static PdfDocument PrintToPdf(this Document document, PrintOptions? printOptions = null, PdfPrintOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.BrowsingContext is null)
            throw new InvalidOperationException("The document is not rendered: it is not the active document of a browsing context.");

        options ??= PdfPrintOptions.Default;
        if (!double.IsFinite(options.RasterizationScale) || options.RasterizationScale <= 0)
            throw new ArgumentException("The rasterization scale must be a positive number.", nameof(options));

        using var session = document.BeginPrint(printOptions ?? PrintOptions.Default);
        session.FetchResources();
        return new PdfDocumentWriter(document, options).Write(session.Paint());
    }

    /// <summary>Prints the active document of a browsing context into a PDF file.</summary>
    /// <param name="browsingContext">The browsing context.</param>
    /// <param name="destination">The stream the PDF file is written to.</param>
    /// <param name="printOptions">The page size and the default margins, or null for <see cref="PrintOptions.Default"/>.</param>
    /// <param name="options">The options of the PDF document, or null for <see cref="PdfPrintOptions.Default"/>.</param>
    /// <exception cref="InvalidOperationException">The document is not rendered, it is already being printed, or it does not meet the requirements of the conformance of <paramref name="options"/>.</exception>
    public static void PrintToPdf(this BrowsingContext browsingContext, Stream destination, PrintOptions? printOptions = null, PdfPrintOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(browsingContext);
        PrintToPdf(browsingContext.Document, destination, printOptions, options);
    }

    /// <summary>Prints a document into a PDF file.</summary>
    /// <param name="document">The document, which must be the active document of a browsing context.</param>
    /// <param name="destination">The stream the PDF file is written to.</param>
    /// <param name="printOptions">The page size and the default margins, or null for <see cref="PrintOptions.Default"/>.</param>
    /// <param name="options">The options of the PDF document, or null for <see cref="PdfPrintOptions.Default"/>.</param>
    /// <exception cref="InvalidOperationException">The document is not rendered, it is already being printed, or it does not meet the requirements of the conformance of <paramref name="options"/>.</exception>
    public static void PrintToPdf(this Document document, Stream destination, PrintOptions? printOptions = null, PdfPrintOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(destination);
        PrintToPdf(document, printOptions, options).Save(destination);
    }
}
