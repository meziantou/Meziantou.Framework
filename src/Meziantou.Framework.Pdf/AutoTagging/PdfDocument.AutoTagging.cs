using Meziantou.Framework.Pdf.AutoTagging;

namespace Meziantou.Framework.Pdf;

public sealed partial class PdfDocument
{
    /// <summary>
    /// Finds the layout of the pages of the document: their headings, paragraphs, lists, tables, figures, headers and
    /// footers, in reading order. The headers and footers are the text repeated at the same place at the top or the
    /// bottom of the pages.
    /// </summary>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="PdfLayoutOptions.Default"/>.</param>
    /// <returns>The layout of each page.</returns>
    /// <exception cref="InvalidPdfContentException">A content stream cannot be decoded.</exception>
    public IReadOnlyList<PdfLayoutPage> ExtractLayout(PdfLayoutOptions? options = null) => LayoutAnalyzer.Analyze(Pages.ToList(), options ?? PdfLayoutOptions.Default);

    /// <summary>
    /// Tags an untagged document (ISO 32000-2, 14.8): its structure tree gets the headings, paragraphs, lists, tables and
    /// figures of the layout of its pages (<see cref="ExtractLayout(PdfLayoutOptions?)"/>), and its content is rewritten
    /// in marked-content sequences of these elements; the headers, the footers, the rules and the shapes are artifacts.
    /// </summary>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="PdfAutoTagOptions.Default"/>.</param>
    /// <returns>What was tagged.</returns>
    /// <remarks>
    /// <para>
    /// The layout is found from the positions and the fonts of the text: check the result, and correct the elements
    /// (<see cref="StructureTree"/>) when needed. A text operator is in one element: a <c>TJ</c> operator that shows the
    /// text of two blocks is in the element of the first one, and a form XObject is in the element of its first text.
    /// The structure is in reading order; the content is not reordered.
    /// </para>
    /// <para>A tagged document can be converted to PDF/A level A (<see cref="ConvertToPdfA(PdfAConformance, PdfAConversionOptions?)"/>).</para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">The document is already tagged.</exception>
    /// <exception cref="InvalidPdfContentException">A content stream cannot be decoded.</exception>
    public PdfAutoTagResult AutoTag(PdfAutoTagOptions? options = null) => AutoTagger.Tag(this, options ?? PdfAutoTagOptions.Default);
}
