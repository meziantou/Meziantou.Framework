namespace Meziantou.Framework.Pdf;

/// <summary>A line of the text of a page: the glyphs that follow each other on a baseline (see <see cref="PdfPage.ExtractTextLayout(PdfTextExtractionOptions?)"/>).</summary>
public sealed class PdfTextLine
{
    internal PdfTextLine(string text, IReadOnlyList<PdfTextWord> words, IReadOnlyList<PdfTextGlyph> glyphs)
    {
        Text = text;
        Words = words;
        Glyphs = glyphs;
        Bounds = Union(glyphs);
    }

    /// <summary>Gets the text of the line, with a space where the glyphs are apart: the line of <see cref="PdfPage.ExtractText()"/>.</summary>
    public string Text { get; }

    /// <summary>Gets the words of the line, in the order of the content.</summary>
    public IReadOnlyList<PdfTextWord> Words { get; }

    /// <summary>Gets the glyphs of the line, in the order of the content, spaces included.</summary>
    public IReadOnlyList<PdfTextGlyph> Glyphs { get; }

    /// <summary>Gets the smallest rectangle that contains the bounds of the glyphs of the line.</summary>
    public PdfRectangle Bounds { get; }

    /// <inheritdoc/>
    public override string ToString() => Text;

    internal static PdfRectangle Union(IReadOnlyList<PdfTextGlyph> glyphs)
    {
        if (glyphs.Count == 0)
            return default;

        var left = double.PositiveInfinity;
        var bottom = double.PositiveInfinity;
        var right = double.NegativeInfinity;
        var top = double.NegativeInfinity;
        foreach (var glyph in glyphs)
        {
            left = Math.Min(left, glyph.Bounds.Left);
            bottom = Math.Min(bottom, glyph.Bounds.Bottom);
            right = Math.Max(right, glyph.Bounds.Right);
            top = Math.Max(top, glyph.Bounds.Top);
        }

        return new PdfRectangle(left, bottom, right, top);
    }
}
