namespace Meziantou.Framework.Pdf;

public sealed partial class PdfPage
{
    /// <summary>
    /// Finds the layout of the page: its headings, paragraphs, lists, tables and figures, in reading order. A single page
    /// has no header nor footer: use <see cref="PdfDocument.ExtractLayout(PdfLayoutOptions?)"/> to find them.
    /// </summary>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="PdfLayoutOptions.Default"/>.</param>
    /// <returns>The layout.</returns>
    /// <exception cref="InvalidPdfContentException">A content stream cannot be decoded.</exception>
    public PdfLayoutPage ExtractLayout(PdfLayoutOptions? options = null) => LayoutAnalyzer.Analyze([this], options ?? PdfLayoutOptions.Default)[0];
}
