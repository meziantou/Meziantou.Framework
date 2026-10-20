using System.Runtime.InteropServices;

namespace Meziantou.Framework.Fonts;

/// <summary>A larger form of a glyph of mathematical layout, and its size along the direction it grows in, in font units.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct MathGlyphVariant(int Glyph, int AdvanceMeasurement);
