using System.Runtime.InteropServices;

namespace Meziantou.Framework.WebPlatform.Css.Fonts;

/// <summary>
/// The font of a run being shaped (HarfBuzz's <c>hb_font_t</c>): a face with the instance of its variation axes. The glyphs,
/// the advances and the extents are in font units.
/// </summary>
/// <param name="Font">The face.</param>
/// <param name="Instance">The instance of the variable font, or null for the default instance.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct ShapingFont(OpenTypeFont Font, FontVariationInstance? Instance)
{
    /// <summary>The number of font units per em (the scale of the positions).</summary>
    public int UnitsPerEm => Font.UnitsPerEm;

    /// <summary>The glyph of a character in the cmap table; returns false when the font does not map it.</summary>
    public bool TryGetNominalGlyph(int codePoint, out int glyph)
    {
        glyph = Font.GetGlyphId(codePoint);
        return glyph != 0;
    }

    /// <summary>Whether the font maps a character.</summary>
    public bool HasGlyph(int codePoint) => Font.GetGlyphId(codePoint) != 0;

    /// <summary>The glyph of a variation sequence (cmap format 14); returns false when the font does not support it.</summary>
    public bool TryGetVariationGlyph(int codePoint, int variationSelector, out int glyph)
    {
        var result = Font.TryGetGlyphId(codePoint, variationSelector, out var value);
        glyph = value;
        return result;
    }

    /// <summary>The horizontal advance of a glyph (hmtx, with the HVAR deltas).</summary>
    public double GetHorizontalAdvance(int glyph) => Font.GetAdvanceWidth((ushort)glyph, Instance);

    /// <summary>The vertical advance of a glyph (vmtx, or the ascent plus the descent).</summary>
    public double GetVerticalAdvance(int glyph) => Font.GetAdvanceHeight((ushort)glyph);

    /// <summary>
    /// The extents of the outline of a glyph, as HarfBuzz's <c>hb_glyph_extents_t</c>: the left side bearing, the top of the
    /// outline (y bearing), the width and the height (negative, downwards). Returns false when the glyph has no outline.
    /// </summary>
    public bool TryGetGlyphExtents(int glyph, out double xBearing, out double yBearing, out double width, out double height)
    {
        if (Font.TryGetGlyphBounds((ushort)glyph, out var bounds))
        {
            xBearing = bounds.XMin;
            yBearing = bounds.YMax;
            width = bounds.XMax - bounds.XMin;
            height = bounds.YMin - bounds.YMax;
            return true;
        }

        xBearing = yBearing = width = height = 0;
        return false;
    }
}
