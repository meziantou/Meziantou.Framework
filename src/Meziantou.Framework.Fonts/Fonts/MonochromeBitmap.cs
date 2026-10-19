using System.Runtime.InteropServices;

namespace Meziantou.Framework.Fonts;

/// <summary>A glyph a font gives as a bitmap of one bit per pixel, for one size: where its bits are in the data table, and where it is drawn.</summary>
/// <param name="Offset">The position of the bits in the data table.</param>
/// <param name="Length">The number of bytes of the bits.</param>
/// <param name="Width">The width of the bitmap, in pixels.</param>
/// <param name="Height">The height of the bitmap, in pixels.</param>
/// <param name="Left">The distance from the origin of the glyph to the left of the bitmap, in pixels.</param>
/// <param name="Top">The distance from the baseline up to the top of the bitmap, in pixels.</param>
/// <param name="PixelsPerEm">The size the bitmap is made for.</param>
/// <param name="IsByteAligned">Whether each row starts at a byte; otherwise the bits of the rows follow each other.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct MonochromeBitmap(int Offset, int Length, int Width, int Height, int Left, int Top, int PixelsPerEm, bool IsByteAligned);
