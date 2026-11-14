using System.Runtime.InteropServices;

namespace Meziantou.Framework.Fonts;

/// <summary>A glyph a font gives as an image of a fixed size: where the image is in the font data, and where it is drawn.</summary>
/// <param name="Offset">The position of the encoded image in the bytes of the font; for <see cref="BitmapGlyphFormat.Mask"/> and <see cref="BitmapGlyphFormat.Bgra"/>, the position of the data of the glyph.</param>
/// <param name="Length">The length of the encoded image, or of the data of the glyph.</param>
/// <param name="PixelsPerEmX">The number of pixels of the image per em, horizontally: the size the image is made for.</param>
/// <param name="PixelsPerEmY">The number of pixels of the image per em, vertically.</param>
/// <param name="Left">The distance from the origin of the glyph to the left of the image, in pixels of the image.</param>
/// <param name="Bottom">The distance from the baseline up to the bottom of the image, in pixels of the image; <see langword="null"/> when the font gives the top instead.</param>
/// <param name="Top">The distance from the baseline up to the top of the image, in pixels of the image.</param>
/// <param name="Format">The format the font says the encoded image has.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct BitmapGlyph(int Offset, int Length, int PixelsPerEmX, int PixelsPerEmY, int Left, int? Bottom, int Top, BitmapGlyphFormat Format)
{
    /// <summary>Gets the width of the image, in pixels, as the font declares it; 0 when only the encoded image gives it.</summary>
    public int Width { get; init; }

    /// <summary>Gets the height of the image, in pixels, as the font declares it; 0 when only the encoded image gives it.</summary>
    public int Height { get; init; }

    /// <summary>Gets the number of bits per pixel of the strike: 1, 2, 4 or 8 for a mask, 32 for colors.</summary>
    public int BitDepth { get; init; }

    /// <summary>Gets the glyph whose image this is.</summary>
    internal int GlyphId { get; init; }

    /// <summary>Gets the position of the record of the strike in its location table, for the glyphs that are not encoded.</summary>
    internal int Strike { get; init; }

    /// <summary>Gets a value indicating whether the glyph is in the tables of the bitmaps without colors (<c>EBLC</c>, <c>bloc</c>) rather than <c>CBLC</c>.</summary>
    internal bool IsMonochromeTable { get; init; }
}
