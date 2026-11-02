namespace Meziantou.Framework.Pdf;

/// <summary>A word of the text of a page: glyphs of a line that no space separates (see <see cref="PdfPage.ExtractTextLayout(PdfTextExtractionOptions?)"/>).</summary>
public sealed class PdfTextWord
{
    internal PdfTextWord(string text, IReadOnlyList<PdfTextGlyph> glyphs)
    {
        Text = text;
        Glyphs = glyphs;
        Bounds = PdfTextLine.Union(glyphs);
    }

    /// <summary>Gets the text of the word: the text of its glyphs.</summary>
    public string Text { get; }

    /// <summary>Gets the glyphs of the word, in the order of the content.</summary>
    public IReadOnlyList<PdfTextGlyph> Glyphs { get; }

    /// <summary>Gets the smallest rectangle that contains the bounds of the glyphs of the word.</summary>
    public PdfRectangle Bounds { get; }

    /// <inheritdoc/>
    public override string ToString() => Text;
}
