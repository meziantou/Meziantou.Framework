using System.Runtime.InteropServices;

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
/// The color glyphs of a font: the layers of the base glyphs of the COLR table, version 0
/// (https://learn.microsoft.com/typography/opentype/spec/colr), and the palettes of the CPAL table
/// (https://learn.microsoft.com/typography/opentype/spec/cpal). A color glyph is drawn as its layers, from the first one
/// (the bottom one) to the last one, each one being the outline of a glyph filled with a color of the selected palette.
/// The paint graphs of the version 1 of the COLR table (gradients, transforms, compositing) are not read: the glyphs that
/// only have a paint graph are not color glyphs here.
/// </summary>
internal sealed class OpenTypeColorGlyphs
{
    /// <summary>The palette index of the layers drawn in the color of the text ("the text foreground color").</summary>
    public const ushort ForegroundPaletteIndex = 0xFFFF;

    // The types of the palettes of the version 1 of the CPAL table
    private const uint UsableWithLightBackground = 0x0001;
    private const uint UsableWithDarkBackground = 0x0002;

    private readonly byte[] _data;
    private readonly int _baseGlyphRecords;
    private readonly int _baseGlyphCount;
    private readonly int _layerRecords;
    private readonly int _layerCount;
    private readonly PaletteColor[][] _palettes;

    private OpenTypeColorGlyphs(byte[] data, int baseGlyphRecords, int baseGlyphCount, int layerRecords, int layerCount, PaletteColor[][] palettes, int paletteEntryCount, int? lightPalette, int? darkPalette)
    {
        _data = data;
        _baseGlyphRecords = baseGlyphRecords;
        _baseGlyphCount = baseGlyphCount;
        _layerRecords = layerRecords;
        _layerCount = layerCount;
        _palettes = palettes;
        PaletteEntryCount = paletteEntryCount;
        LightPalette = lightPalette;
        DarkPalette = darkPalette;
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
    /// Reads the COLR and CPAL tables of a font. Returns null when the font has no base glyph record (it has no color glyph,
    /// or only the paint graphs of the version 1 of the table).
    /// </summary>
    internal static OpenTypeColorGlyphs? Create(byte[] data, (int Offset, int Length) colrRange, (int Offset, int Length) cpalRange)
    {
        if (colrRange.Offset < 0 || colrRange.Length < 14)
            return null;

        // The header of the version 0, which the version 1 starts with
        var colr = data.AsSpan(colrRange.Offset, colrRange.Length);
        var baseGlyphCount = FontBinary.U16(colr, 2);
        var baseGlyphRecords = (long)FontBinary.U32(colr, 4);
        var layerRecords = (long)FontBinary.U32(colr, 8);
        var layerCount = FontBinary.U16(colr, 12);
        if (baseGlyphCount == 0 || !FontBinary.IsInRange(colr, baseGlyphRecords, baseGlyphCount * 6L) || !FontBinary.IsInRange(colr, layerRecords, layerCount * 4L))
            return null;

        var palettes = ReadPalettes(data, cpalRange, out var entryCount, out var lightPalette, out var darkPalette);
        return new OpenTypeColorGlyphs(data, colrRange.Offset + (int)baseGlyphRecords, baseGlyphCount, colrRange.Offset + (int)layerRecords, layerCount, palettes, entryCount, lightPalette, darkPalette);
    }

    private static PaletteColor[][] ReadPalettes(byte[] data, (int Offset, int Length) range, out int entryCount, out int? lightPalette, out int? darkPalette)
    {
        entryCount = 0;
        lightPalette = null;
        darkPalette = null;
        if (range.Offset < 0 || range.Length < 12)
            return [];

        var cpal = data.AsSpan(range.Offset, range.Length);
        var version = FontBinary.U16(cpal, 0);
        entryCount = FontBinary.U16(cpal, 2);
        var paletteCount = FontBinary.U16(cpal, 4);
        var colorRecordCount = FontBinary.U16(cpal, 6);
        var colorRecords = (long)FontBinary.U32(cpal, 8);
        if (!FontBinary.IsInRange(cpal, 12, paletteCount * 2L) || !FontBinary.IsInRange(cpal, colorRecords, colorRecordCount * 4L))
        {
            entryCount = 0;
            return [];
        }

        var palettes = new PaletteColor[paletteCount][];
        for (var i = 0; i < paletteCount; i++)
        {
            // The colors of a palette that are outside of the color records are transparent black
            var first = FontBinary.U16(cpal, 12 + (i * 2));
            var colors = new PaletteColor[entryCount];
            for (var j = 0; j < entryCount && first + j < colorRecordCount; j++)
            {
                // A ColorRecord is blue, green, red, alpha
                var record = (int)colorRecords + ((first + j) * 4);
                colors[j] = new PaletteColor(cpal[record + 2], cpal[record + 1], cpal[record], cpal[record + 3]);
            }

            palettes[i] = colors;
        }

        // The version 1 has the types of the palettes after the indexes of their first color records
        var types = version >= 1 ? (long)FontBinary.U32(cpal, 12 + (paletteCount * 2)) : 0;
        if (types != 0 && FontBinary.IsInRange(cpal, types, paletteCount * 4L))
        {
            for (var i = 0; i < paletteCount; i++)
            {
                var type = FontBinary.U32(cpal, (int)types + (i * 4));
                if ((type & UsableWithLightBackground) != 0)
                {
                    lightPalette ??= i;
                }

                if ((type & UsableWithDarkBackground) != 0)
                {
                    darkPalette ??= i;
                }
            }
        }

        return palettes;
    }

    /// <summary>Returns whether a glyph is a color glyph (a base glyph of the COLR table).</summary>
    public bool IsColorGlyph(ushort glyphId) => FindBaseGlyphRecord(glyphId) >= 0;

    /// <summary>
    /// Returns the layers of a glyph, from the bottom one to the top one, when it is a color glyph (a base glyph of the COLR
    /// table). The layers whose palette entry does not exist are not returned: a color glyph may have no layer to draw.
    /// </summary>
    public bool TryGetLayers(ushort glyphId, out ColorGlyphLayer[] layers)
    {
        var record = FindBaseGlyphRecord(glyphId);
        if (record < 0)
        {
            layers = [];
            return false;
        }

        var data = _data.AsSpan();
        var first = FontBinary.U16(data, record + 2);
        var count = Math.Min(FontBinary.U16(data, record + 4), Math.Max(0, _layerCount - first));
        var result = new List<ColorGlyphLayer>(count);
        for (var i = 0; i < count; i++)
        {
            var layer = _layerRecords + ((first + i) * 4);
            var paletteIndex = FontBinary.U16(data, layer + 2);
            if (paletteIndex == ForegroundPaletteIndex || paletteIndex < PaletteEntryCount)
            {
                result.Add(new ColorGlyphLayer(FontBinary.U16(data, layer), paletteIndex));
            }
        }

        layers = [.. result];
        return true;
    }

    /// <summary>Returns the offset of the base glyph record of a glyph in the data of the font, or -1.</summary>
    private int FindBaseGlyphRecord(ushort glyphId)
    {
        // The base glyph records are sorted by glyph id
        var data = _data.AsSpan();
        var low = 0;
        var high = _baseGlyphCount - 1;
        while (low <= high)
        {
            var middle = (low + high) >>> 1;
            var record = _baseGlyphRecords + (middle * 6);
            var glyph = FontBinary.U16(data, record);
            if (glyph == glyphId)
                return record;

            if (glyph < glyphId)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return -1;
    }
}
