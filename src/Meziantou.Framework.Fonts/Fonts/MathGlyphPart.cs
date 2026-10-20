using System.Runtime.InteropServices;

namespace Meziantou.Framework.Fonts;

/// <summary>
/// A part of the assembly of a glyph of mathematical layout: the glyph, the lengths of its connectors at its start and
/// at its end, its full size along the direction of the assembly (in font units), and whether it can be repeated.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct MathGlyphPart(int Glyph, int StartConnectorLength, int EndConnectorLength, int FullAdvance, bool IsExtender);
