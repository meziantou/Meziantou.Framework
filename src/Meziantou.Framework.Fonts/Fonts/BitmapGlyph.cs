using System.Runtime.InteropServices;

namespace Meziantou.Framework.Fonts;

/// <summary>A glyph a font gives as an image of a fixed size: where the image is in the font data, and where it is drawn.</summary>
/// <param name="Offset">The position of the encoded image in the bytes of the font.</param>
/// <param name="Length">The length of the encoded image.</param>
/// <param name="PixelsPerEmX">The number of pixels of the image per em, horizontally: the size the image is made for.</param>
/// <param name="PixelsPerEmY">The number of pixels of the image per em, vertically.</param>
/// <param name="Left">The distance from the origin of the glyph to the left of the image, in pixels of the image.</param>
/// <param name="Bottom">The distance from the baseline up to the bottom of the image, in pixels of the image; <see langword="null"/> when the font gives the top instead.</param>
/// <param name="Top">The distance from the baseline up to the top of the image, in pixels of the image.</param>
/// <param name="Format">The format the font says the encoded image has.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct BitmapGlyph(int Offset, int Length, int PixelsPerEmX, int PixelsPerEmY, int Left, int? Bottom, int Top, BitmapGlyphFormat Format);
