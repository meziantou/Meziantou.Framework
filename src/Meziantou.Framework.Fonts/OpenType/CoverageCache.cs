namespace Meziantou.Framework.Fonts;

/// <summary>
/// The coverage indexes of a coverage table, kept by pages of 256 glyphs built the first time a glyph of the page is
/// looked up: the glyphs of a text are looked up in the coverage of each subtable of each lookup, again and again.
/// </summary>
/// <remarks>Like the <see cref="LookupDigests"/> that keep it, a cache is used by one shaping at a time.</remarks>
internal sealed class CoverageCache
{
    private const ushort NotCovered = ushort.MaxValue;

    private readonly ushort[]?[] _pages = new ushort[]?[256];

    public CoverageCache(int offset)
    {
        Offset = offset;
    }

    /// <summary>Gets the offset of the coverage table in its layout table.</summary>
    public int Offset { get; }

    /// <summary>Gets the coverage index of a glyph, or <see cref="LayoutCommon.NotCovered"/>.</summary>
    /// <exception cref="InvalidFontException">The coverage table is malformed.</exception>
    public int Get(ReadOnlySpan<byte> table, int glyph)
    {
        if ((uint)glyph > 0xFFFF)
            return LayoutCommon.GetCoverage(table, Offset, glyph);

        var page = _pages[glyph >> 8];
        if (page is null)
        {
            page = new ushort[256];
            var first = glyph & ~0xFF;
            try
            {
                for (var i = 0; i < page.Length; i++)
                {
                    var index = LayoutCommon.GetCoverage(table, Offset, first + i);
                    page[i] = index is >= 0 and < NotCovered ? (ushort)index : NotCovered;
                }
            }
            catch (InvalidFontException)
            {
                // A malformed table fails for the glyphs that read past its end, as it does when they are looked up.
                return LayoutCommon.GetCoverage(table, Offset, glyph);
            }

            _pages[glyph >> 8] = page;
        }

        var value = page[glyph & 0xFF];
        return value == NotCovered ? LayoutCommon.NotCovered : value;
    }
}
