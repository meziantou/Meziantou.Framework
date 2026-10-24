using System.Runtime.InteropServices;

namespace Meziantou.Framework.Fonts;

/// <summary>A bounding box of an AFM file, in units of 1/1000 of the point size, with the y axis up.</summary>
/// <remarks>The numbers can be reals, unlike the font units of <see cref="GlyphBox"/>.</remarks>
/// <param name="Left">The x coordinate of the lower left corner (llx).</param>
/// <param name="Bottom">The y coordinate of the lower left corner (lly).</param>
/// <param name="Right">The x coordinate of the upper right corner (urx).</param>
/// <param name="Top">The y coordinate of the upper right corner (ury).</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct AfmBox(double Left, double Bottom, double Right, double Top);
