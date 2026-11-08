namespace Meziantou.Framework.Pdf;

/// <summary>The options of <see cref="PdfPage.ExtractText(PdfTextExtractionOptions?)"/> and <see cref="PdfPage.ExtractTextLayout(PdfTextExtractionOptions?)"/>.</summary>
public sealed class PdfTextExtractionOptions
{
    /// <summary>Gets the default options: the text exactly as the fonts give it.</summary>
    public static PdfTextExtractionOptions Default { get; } = new();

    /// <summary>
    /// Gets a value indicating whether the Latin ligatures of the Alphabetic Presentation Forms block (U+FB00 to U+FB06:
    /// <c>ﬀ</c>, <c>ﬁ</c>, <c>ﬂ</c>, <c>ﬃ</c>, <c>ﬄ</c>, <c>ﬅ</c>, <c>ﬆ</c>) are replaced by the letters they join, so that a
    /// search for "office" finds a word shown with the glyph <c>ﬃ</c>. The default is <see langword="false"/>: the text is
    /// what the fonts map their glyphs to.
    /// </summary>
    public bool ExpandLigatures { get; init; }

    /// <summary>
    /// Gets the order of the lines: the order of the content (<see cref="PdfTextReadingOrder.Content"/>, the default), or
    /// the reading order found from the layout of the page (<see cref="PdfTextReadingOrder.Layout"/>), which reads the
    /// columns of a page one after the other.
    /// </summary>
    public PdfTextReadingOrder ReadingOrder { get; init; }
}
