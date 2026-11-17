using System.Runtime.InteropServices;

namespace Meziantou.Framework.Fonts;

/// <summary>A glyph a font describes with an SVG document (the <c>SVG </c> table): where the document is in the font data.</summary>
/// <param name="GlyphId">The glyph: the document has an element whose identifier is <c>glyph</c> followed by this number.</param>
/// <param name="FirstGlyphId">The first glyph of the range of glyphs the document describes.</param>
/// <param name="LastGlyphId">The last glyph of the range of glyphs the document describes.</param>
/// <param name="Offset">The position of the document in the bytes of the font.</param>
/// <param name="Length">The length of the document in the font, compressed or not.</param>
/// <param name="IsCompressed">Whether the document is compressed with gzip; <see cref="FontFace.GetSvgDocument"/> expands it.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct SvgGlyph(int GlyphId, int FirstGlyphId, int LastGlyphId, int Offset, int Length, bool IsCompressed);
