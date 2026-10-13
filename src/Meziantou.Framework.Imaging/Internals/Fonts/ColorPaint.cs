using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>A paint of the display list of a color glyph: a color, or a gradient in its own coordinate space.</summary>
/// <param name="Kind">The kind of paint.</param>
/// <param name="Transform">The index, in the numbers of the color glyph, of the six values of the transformation from the space of the paint to the space of the glyph; -1 for none.</param>
/// <param name="Geometry">The index, in the numbers, of the geometry of a gradient.</param>
/// <param name="FirstStop">The index of the first stop of a gradient, or of the color of a solid paint.</param>
/// <param name="StopCount">The number of stops of a gradient.</param>
/// <param name="Extend">How a gradient continues outside its color line: 0 pads, 1 repeats, 2 reflects.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct ColorPaint(ColorPaintKind Kind, int Transform, int Geometry, int FirstStop, int StopCount, int Extend);
