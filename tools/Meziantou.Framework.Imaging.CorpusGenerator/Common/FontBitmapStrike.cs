namespace Meziantou.Framework.Imaging.CorpusGenerator.Common;

/// <summary>A strike of a bitmap font under construction: the images of glyphs at a number of pixels per em.</summary>
internal sealed class FontBitmapStrike
{
    public required int PixelsPerEm { get; init; }

    public List<FontBitmapGlyph> Glyphs { get; } = [];

    /// <summary>
    /// Gets or sets the format of the index subtables of the <c>CBLC</c> table: 1 or 3 for an offset per glyph (32 or
    /// 16 bits), 2 or 5 for images of one size with the same metrics (a range or a list of glyphs), 4 for pairs of a
    /// glyph and an offset.
    /// </summary>
    public int IndexFormat { get; init; } = 1;

    /// <summary>
    /// Gets or sets the format of the images. In the <c>CBDT</c> table: 17 or 18 for small or big metrics before each
    /// image, 19 for the metrics of the index subtable. In the <c>EBDT</c> table: 1 or 2 for small metrics, 6 or 7 for
    /// big metrics, 5 for the metrics of the index subtable; the rows of the formats 1 and 6 each start at a byte.
    /// </summary>
    public int ImageFormat { get; init; } = 17;

    /// <summary>Gets or sets the number of bits per pixel of a strike of the <c>EBLC</c> table: 1 for the bitmaps that are drawn.</summary>
    public int BitDepth { get; init; } = 1;
}
