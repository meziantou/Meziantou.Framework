namespace Meziantou.Framework.Fonts;

/// <summary>The format of the image of a bitmap glyph (<c>sbix</c>, <c>CBDT</c>, <c>EBDT</c>, <c>bdat</c>).</summary>
public enum BitmapGlyphFormat
{
    /// <summary>A PNG image.</summary>
    Png,

    /// <summary>A JPEG image.</summary>
    Jpeg,

    /// <summary>A TIFF image.</summary>
    Tiff,

    /// <summary>
    /// Pixels that are not encoded, of 1, 2, 4 or 8 bits each: the coverage of the pixel, drawn with the color of the
    /// text. <see cref="FontFace.DecodeBitmap"/> decodes them.
    /// </summary>
    Mask,

    /// <summary>
    /// Pixels that are not encoded, of 32 bits each: blue, green, red and opacity, the colors multiplied by the
    /// opacity. <see cref="FontFace.DecodeBitmap"/> decodes them.
    /// </summary>
    Bgra,
}
