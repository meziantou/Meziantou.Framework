namespace Meziantou.Framework.Pdf;

/// <summary>The text of a page with the position of its lines, words and glyphs (see <see cref="PdfPage.ExtractTextLayout(PdfTextExtractionOptions?)"/>).</summary>
/// <remarks>
/// By default, the lines are in the order of the content, not in a reading order: a new line starts where a glyph leaves
/// the baseline of the previous one, goes back, or changes direction. With <see cref="PdfTextReadingOrder.Layout"/>, the
/// lines are split at the wide gaps, grouped into blocks at the blank spaces of the page (columns, paragraphs), and
/// ordered by their position.
/// </remarks>
public sealed class PdfTextLayout
{
    internal PdfTextLayout(string text, IReadOnlyList<PdfTextLine> lines, IReadOnlyList<PdfTextBlock> blocks)
    {
        Text = text;
        Lines = lines;
        Blocks = blocks;
    }

    /// <summary>Gets the text of the page: the text of the lines separated by line feeds, the result of <see cref="PdfPage.ExtractText(PdfTextExtractionOptions?)"/>.</summary>
    public string Text { get; }

    /// <summary>Gets the lines of the page, in the order of the content or in the reading order (<see cref="PdfTextExtractionOptions.ReadingOrder"/>).</summary>
    public IReadOnlyList<PdfTextLine> Lines { get; }

    /// <summary>
    /// Gets the blocks of the page, in the order of <see cref="Lines"/>: with <see cref="PdfTextReadingOrder.Layout"/>, the
    /// groups of lines the blank spaces of the page separate; otherwise, one block that has all the lines (none when the
    /// page has no text).
    /// </summary>
    public IReadOnlyList<PdfTextBlock> Blocks { get; }

    /// <inheritdoc/>
    public override string ToString() => Text;
}
