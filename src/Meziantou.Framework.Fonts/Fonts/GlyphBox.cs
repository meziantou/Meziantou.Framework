using System.Runtime.InteropServices;

namespace Meziantou.Framework.Fonts;

/// <summary>The box of the outline of a glyph, in font units, with the y axis up.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct GlyphBox(int XMin, int YMin, int XMax, int YMax);
