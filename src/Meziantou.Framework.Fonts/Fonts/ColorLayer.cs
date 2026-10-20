using System.Runtime.InteropServices;

namespace Meziantou.Framework.Fonts;

/// <summary>
/// A layer of a color glyph of version 0 of the <c>COLR</c> table: the glyph whose outline it fills, and the index of
/// its color in the palette (<see cref="CpalTable.Foreground"/> for the color of the text).
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct ColorLayer(int Glyph, int PaletteIndex);
