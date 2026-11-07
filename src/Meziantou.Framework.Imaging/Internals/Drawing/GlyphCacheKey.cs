using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// What a glyph computes from: the coverage of an outline (the linear part of the transformation from font units to
/// pixels, the position of the origin in its pixel in quarters, antialiasing and synthetic bold), or the display list of a
/// glyph a font draws with its own colors (its palette, and the size of a bitmap).
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct GlyphCacheKey(FontFace Face, int Glyph, bool IsColor, double Size, int Palette, double A, double B, double C, double D, int FractionX, int FractionY, bool IsAliased, double BoldWidth);
