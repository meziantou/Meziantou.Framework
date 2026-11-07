using System.Runtime.InteropServices;
using SharedFontException = Meziantou.Framework.Fonts.FontException;
using SharedFontFace = Meziantou.Framework.Fonts.FontFace;

namespace Meziantou.Framework.WebPlatform.Css.Fonts;

/// <summary>
/// A layer of a color glyph (a Layer record of the COLR table): the glyph whose outline is filled, and the entry of the
/// palette that gives its color (<see cref="OpenTypeColorGlyphs.ForegroundPaletteIndex"/> for the color of the text).
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct ColorGlyphLayer(ushort GlyphId, ushort PaletteIndex);

/// <summary>A color of a palette of the CPAL table (a ColorRecord): the sRGB components and the alpha, not premultiplied.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct PaletteColor(byte Red, byte Green, byte Blue, byte Alpha);

/// <summary>
/// The color glyphs of a font, read by the font reader: the layers of the base glyphs of the COLR table, version 0
/// (https://learn.microsoft.com/typography/opentype/spec/colr), and the palettes of the CPAL table
/// (https://learn.microsoft.com/typography/opentype/spec/cpal). A color glyph is drawn as its layers, from the first one
/// (the bottom one) to the last one, each one being the outline of a glyph filled with a color of the selected palette.
/// The paint graphs of the version 1 of the COLR table (gradients, transforms, compositing) are not used: the glyphs that
/// only have a paint graph are not color glyphs here.
/// </summary>
internal sealed class OpenTypeColorGlyphs
{
    /// <summary>The palette index of the layers drawn in the color of the text ("the text foreground color").</summary>
    public const ushort ForegroundPaletteIndex = 0xFFFF;

    private readonly SharedFontFace _face;
    private readonly PaletteColor[][] _palettes;

    private OpenTypeColorGlyphs(SharedFontFace face)
    {
        _face = face;
        PaletteEntryCount = face.PaletteEntryCount;
        _palettes = new PaletteColor[face.PaletteCount][];
        for (var i = 0; i < _palettes.Length; i++)
        {
            // The colors of a palette that are outside of the color records are transparent black
            var colors = new PaletteColor[PaletteEntryCount];
            for (var j = 0; j < colors.Length; j++)
            {
                if (face.TryGetPaletteColor(i, j, out var red, out var green, out var blue, out var alpha))
                {
                    colors[j] = new PaletteColor((byte)red, (byte)green, (byte)blue, (byte)alpha);
                }
            }

            _palettes[i] = colors;
            face.GetPaletteType(i, out var isForLightBackground, out var isForDarkBackground);
            if (isForLightBackground)
            {
                LightPalette ??= i;
            }

            if (isForDarkBackground)
            {
                DarkPalette ??= i;
            }
        }
    }

    /// <summary>The number of palettes of the font (0 when the font has no CPAL table: only the color of the text is used).</summary>
    public int PaletteCount => _palettes.Length;

    /// <summary>The number of colors of each palette.</summary>
    public int PaletteEntryCount { get; }

    /// <summary>The index of the first palette marked as usable with a light background, or null.</summary>
    public int? LightPalette { get; }

    /// <summary>The index of the first palette marked as usable with a dark background, or null.</summary>
    public int? DarkPalette { get; }

    /// <summary>The colors of a palette.</summary>
    public ReadOnlySpan<PaletteColor> GetPalette(int index) => _palettes[index];

    /// <summary>
    /// Creates the color glyphs of a font. Returns null when the font has no base glyph record (it has no color glyph, or
    /// only the paint graphs of the version 1 of the COLR table).
    /// </summary>
    internal static OpenTypeColorGlyphs? Create(SharedFontFace face) => face.HasColorLayers ? new OpenTypeColorGlyphs(face) : null;

    /// <summary>Returns whether a glyph is a color glyph (a base glyph of the COLR table).</summary>
    public bool IsColorGlyph(ushort glyphId) => _face.IsLayeredColorGlyph(glyphId);

    /// <summary>
    /// Returns the layers of a glyph, from the bottom one to the top one, when it is a color glyph (a base glyph of the COLR
    /// table). The layers whose palette entry does not exist are not returned: a color glyph may have no layer to draw.
    /// </summary>
    public bool TryGetLayers(ushort glyphId, out ColorGlyphLayer[] layers)
    {
        layers = [];
        Meziantou.Framework.Fonts.ColorLayer[]? records;
        try
        {
            records = _face.GetColorLayers(glyphId);
        }
        catch (SharedFontException)
        {
            // The layers of the glyph are outside the layers of the table: it has no layer to draw
            return true;
        }

        if (records is null)
            return false;

        var result = new List<ColorGlyphLayer>(records.Length);
        foreach (var record in records)
        {
            if (record.PaletteIndex == ForegroundPaletteIndex || record.PaletteIndex < PaletteEntryCount)
            {
                result.Add(new ColorGlyphLayer((ushort)record.Glyph, (ushort)record.PaletteIndex));
            }
        }

        layers = [.. result];
        return true;
    }
}
