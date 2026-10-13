using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>An operation of the display list of a color glyph.</summary>
/// <param name="Kind">What the operation does.</param>
/// <param name="Glyph">The glyph whose outline is filled or clips.</param>
/// <param name="Transform">The index, in the numbers of the color glyph, of the six values of the transformation of the outline or of the box; -1 for none.</param>
/// <param name="Paint">The index of the paint of a fill; the composition mode of the end of a layer; the index, in the numbers, of the four sides of a box.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct ColorOp(ColorOpKind Kind, int Glyph, int Transform, int Paint);
