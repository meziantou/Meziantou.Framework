using Meziantou.Framework.Pdf;

namespace Meziantou.Framework.WebPlatform.PdfRendering;

/// <summary>The options of the PDF documents printed by <see cref="PdfPrintExtensions"/>.</summary>
public sealed record PdfPrintOptions
{
    /// <summary>Gets the default options.</summary>
    public static PdfPrintOptions Default { get; } = new();

    /// <summary>
    /// Gets a value indicating whether the document is a tagged PDF (ISO 32000-2, 14.8): a structure tree made from the
    /// elements of the document (headings, paragraphs, lists, tables, figures, links), so that assistive technologies can
    /// read it, and the decorations (backgrounds, borders, page-margin boxes) marked as artifacts. The default is <see langword="true"/>.
    /// </summary>
    /// <remarks>The document is always tagged when <see cref="PdfUAConformance"/> is set, or when <see cref="PdfAConformance"/> is a level A conformance.</remarks>
    public bool Tagged { get; init; } = true;

    /// <summary>Gets a value indicating whether the links of the document (<c>a</c> elements with an <c>href</c>) are link annotations. The default is <see langword="true"/>.</summary>
    public bool IncludeLinks { get; init; } = true;

    /// <summary>Gets a value indicating whether the headings of the document (<c>h1</c> to <c>h6</c>) are the outline (bookmarks) of the document. The default is <see langword="true"/>.</summary>
    public bool IncludeOutlines { get; init; } = true;

    /// <summary>Gets the PDF/UA conformance of the document. The document needs a title (<see cref="Title"/> or the <c>title</c> element) and a language (the <c>lang</c> attribute of the root element).</summary>
    public PdfUAConformance PdfUAConformance { get; init; }

    /// <summary>Gets the PDF/A conformance of the document.</summary>
    public PdfAConformance PdfAConformance { get; init; }

    /// <summary>Gets the title of the document, or <see langword="null"/> for the title of the HTML document (its <c>title</c> element).</summary>
    public string? Title { get; init; }

    /// <summary>
    /// Gets the number of image pixels per CSS pixel of the parts of the pages that PDF cannot draw as vector graphics
    /// (blurred shadows, filters, some compositing operators, 3D transforms), which are drawn as images. The default is 2
    /// (192 pixels per inch).
    /// </summary>
    public double RasterizationScale { get; init; } = 2;
}
