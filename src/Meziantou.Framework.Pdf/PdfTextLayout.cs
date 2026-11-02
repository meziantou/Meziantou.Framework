namespace Meziantou.Framework.Pdf;

/// <summary>The text of a page with the position of its lines, words and glyphs (see <see cref="PdfPage.ExtractTextLayout(PdfTextExtractionOptions?)"/>).</summary>
/// <remarks>
/// The lines are in the order of the content, not in a reading order: a new line starts where a glyph leaves the
/// baseline of the previous one, goes back, or changes direction. Columns, tables and text painted out of order are not
/// detected; the positions let an application order or group them.
/// </remarks>
public sealed class PdfTextLayout
{
    internal PdfTextLayout(string text, IReadOnlyList<PdfTextLine> lines)
    {
        Text = text;
        Lines = lines;
    }

    /// <summary>Gets the text of the page: the text of the lines separated by line feeds, the result of <see cref="PdfPage.ExtractText(PdfTextExtractionOptions?)"/>.</summary>
    public string Text { get; }

    /// <summary>Gets the lines of the page, in the order of the content.</summary>
    public IReadOnlyList<PdfTextLine> Lines { get; }

    /// <inheritdoc/>
    public override string ToString() => Text;
}
