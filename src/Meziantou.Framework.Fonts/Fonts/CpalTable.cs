namespace Meziantou.Framework.Fonts;

/// <summary>Reads the <c>CPAL</c> table: the palettes of colors the glyphs of a color font are painted with.</summary>
/// <remarks>
/// OpenType specification, "CPAL - Color Palette Table". A palette is a list of colors; every palette of a font has the
/// same number of entries, and a paint names an entry. The colors are sRGB values with an opacity, not multiplied by it.
/// </remarks>
internal sealed class CpalTable
{
    /// <summary>The entry that stands for the color of the text instead of a color of the palette.</summary>
    public const int Foreground = 0xFFFF;

    private const int UsableWithLightBackground = 0x0001;
    private const int UsableWithDarkBackground = 0x0002;

    private readonly int _colors;
    private readonly int _colorCount;
    private readonly int _types;

    private CpalTable(int entryCount, int paletteCount, int colors, int colorCount, int types)
    {
        EntryCount = entryCount;
        PaletteCount = paletteCount;
        _colors = colors;
        _colorCount = colorCount;
        _types = types;
    }

    /// <summary>Gets the number of colors of each palette.</summary>
    public int EntryCount { get; }

    public int PaletteCount { get; }

    /// <summary>Reads the header of the table, or returns <see langword="null"/> when it has no palette or a version that is not known.</summary>
    public static CpalTable? Read(ReadOnlySpan<byte> table)
    {
        if (table.Length < 12)
            return null;

        var version = FontBinary.UInt16(table, 0);
        var entryCount = FontBinary.UInt16(table, 2);
        var paletteCount = FontBinary.UInt16(table, 4);
        var colorCount = FontBinary.UInt16(table, 6);
        var colors = FontBinary.Offset32(table, 8);
        if (version > 1 || paletteCount == 0)
            return null;

        FontBinary.EnsureRange(table, 12, paletteCount * 2L);
        FontBinary.EnsureRange(table, colors, colorCount * 4L);

        // Version 1 says, for each palette, the backgrounds it is made for.
        var types = 0;
        if (version == 1 && table.Length >= 16 + (paletteCount * 2))
        {
            types = FontBinary.Offset32(table, 12 + (paletteCount * 2));
            if (types != 0)
            {
                FontBinary.EnsureRange(table, types, paletteCount * 4L);
            }
        }

        return new CpalTable(entryCount, paletteCount, colors, colorCount, types);
    }

    /// <summary>Gets a color of a palette: its red, green, blue and alpha components, from 0 to 255.</summary>
    /// <returns><see langword="false"/> when the palette does not have the entry.</returns>
    public bool TryGetColor(ReadOnlySpan<byte> table, int palette, int entry, out int red, out int green, out int blue, out int alpha)
    {
        red = green = blue = alpha = 0;
        if ((uint)palette >= (uint)PaletteCount || (uint)entry >= (uint)EntryCount)
            return false;

        var index = FontBinary.UInt16(table, 12 + (palette * 2)) + entry;
        if (index >= _colorCount)
            return false;

        // A color record: blue, green, red, alpha.
        var record = _colors + (index * 4);
        blue = table[record];
        green = table[record + 1];
        red = table[record + 2];
        alpha = table[record + 3];
        return true;
    }

    /// <summary>Tells the backgrounds a palette is made for.</summary>
    public void GetPaletteType(ReadOnlySpan<byte> table, int palette, out bool isForLightBackground, out bool isForDarkBackground)
    {
        var flags = _types == 0 || (uint)palette >= (uint)PaletteCount ? 0u : FontBinary.UInt32(table, _types + (palette * 4));
        isForLightBackground = (flags & UsableWithLightBackground) != 0;
        isForDarkBackground = (flags & UsableWithDarkBackground) != 0;
    }
}
