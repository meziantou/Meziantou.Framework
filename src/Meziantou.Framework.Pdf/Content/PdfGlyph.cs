using System.Runtime.InteropServices;

namespace Meziantou.Framework.Pdf;

/// <summary>A glyph shown at a position by <see cref="PdfContentBuilder.ShowGlyphs"/>.</summary>
/// <param name="GlyphId">The identifier of the glyph in the font.</param>
/// <param name="X">The horizontal position of the origin of the glyph from the current text position, in unscaled text space units (as the operands of <c>Td</c>).</param>
/// <param name="Y">The vertical position of the origin of the glyph above the baseline, in unscaled text space units.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct PdfGlyph(int GlyphId, double X, double Y);
