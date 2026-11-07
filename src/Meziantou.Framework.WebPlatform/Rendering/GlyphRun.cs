using Meziantou.Framework.WebPlatform.Css.Fonts;
using Meziantou.Framework.WebPlatform.Css.Layout;

namespace Meziantou.Framework.WebPlatform.Rendering;

/// <summary>
/// The font of a <see cref="GlyphRun"/>: a face of a font file decoded by the managed font reader (the sfnt data of
/// <see cref="FontFile.Data"/>, after the WOFF/WOFF2 decoding, and the index of the face in a collection), at a size, with the
/// values of its variation axes and the synthesized styles. A backend creates its typeface from the same bytes, so the glyph
/// ids and the metrics are the ones the layout used.
/// </summary>
/// <param name="File">The font file (its <see cref="FontFile.Data"/> are the bytes of an OpenType font or collection).</param>
/// <param name="FaceIndex">The index of the face in the font file (0 for a font that is not a collection).</param>
/// <param name="Size">The em size in CSS px.</param>
/// <param name="Variations">The values of the variation axes in user coordinates (e.g. <c>wght</c> 700), in order (a later value of an axis wins); empty for the default instance.</param>
/// <param name="SyntheticBold">Whether the glyphs are emboldened (https://drafts.csswg.org/css-fonts-4/#font-synthesis-weight).</param>
/// <param name="SyntheticObliqueAngle">The angle in degrees the glyphs are slanted by (https://drafts.csswg.org/css-fonts-4/#font-synthesis-style), clockwise, or 0.</param>
internal sealed record DisplayFont(FontFile File, int FaceIndex, double Size, FontVariationSetting[] Variations, bool SyntheticBold, double SyntheticObliqueAngle)
{
    /// <summary>The full name of the face, for the dumps.</summary>
    public string Name => File.Faces is { } faces && FaceIndex < faces.Count ? faces[FaceIndex].FullName : "";

    /// <summary>
    /// Whether the glyphs are drawn without antialiasing, at whole device pixels: the glyphs of the Ahem font (the font of the
    /// tests, whose glyphs are rectangles of the em box). The browsers do the same so that an Ahem glyph paints the same pixels
    /// as a box of its size whatever the font size (the ascent of a 16px Ahem glyph is 12.8px): WebKit never antialiases the
    /// font whose family name is Ahem, Gecko has a preference for it (<c>gfx.font_rendering.ahem_antialias_none</c>, set by the
    /// test profiles), the web tests of Chromium disable the antialiasing of every font, and on Linux the hinting of FreeType
    /// moves the edges of the glyphs to the pixels.
    /// </summary>
    public bool IsAliased => File.Faces is { } faces && FaceIndex < faces.Count && string.Equals(faces[FaceIndex].FamilyName, "Ahem", StringComparison.OrdinalIgnoreCase);

    /// <summary>Creates the font of the glyphs of a matched font at an em size.</summary>
    public static DisplayFont From(MatchedFont font, double emSize)
        => new(font.Font.File, font.Font.FaceIndex, emSize, font.GetVariationSettings(emSize), font.SyntheticBold, font.SyntheticObliqueAngle);
}

/// <summary>
/// A run of glyphs of one font drawn horizontally: the glyph ids of the font and the position of the origin of each glyph (the
/// left end of its alphabetic baseline) in the coordinates of the display list. The glyphs are shaped and positioned by the
/// managed text shaping (#338), so the painting uses the advances the layout measured; a vertical or rotated run is drawn in a
/// transformed coordinate system.
/// </summary>
/// <param name="Font">The font of the glyphs.</param>
/// <param name="Glyphs">The glyph ids.</param>
/// <param name="Positions">The origin of each glyph.</param>
/// <param name="Text">The characters of the glyphs (for the dumps, the contentful paint and the text extraction of a backend), or an empty string.</param>
internal sealed record GlyphRun(DisplayFont Font, ushort[] Glyphs, PhysicalOffset[] Positions, string Text)
{
    /// <summary>The horizontal scale of the glyphs (a composition combined upright is compressed, https://drafts.csswg.org/css-writing-modes-4/#text-combine-compression), or 1.</summary>
    public double ScaleX { get; init; } = 1;
}
