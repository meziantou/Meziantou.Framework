using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// A set of glyphs that may contain more glyphs than were added, never fewer: three masks of 64 bits, each with the bit
/// of a part of the index of every glyph added (its low 6 bits, then the 6 bits from bit 4 and from bit 9).
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal struct GlyphDigest
{
    private const int Shift1 = 4;
    private const int Shift2 = 9;

    private ulong _mask0;
    private ulong _mask1;
    private ulong _mask2;

    /// <summary>Gets a digest that contains every glyph.</summary>
    public static GlyphDigest All => new() { _mask0 = ulong.MaxValue, _mask1 = ulong.MaxValue, _mask2 = ulong.MaxValue };

    /// <summary>Determines whether the set may contain a glyph: <see langword="false"/> when it does not.</summary>
    public readonly bool MayContain(int glyph)
        => ((_mask0 >> (glyph & 63)) & (_mask1 >> ((glyph >> Shift1) & 63)) & (_mask2 >> ((glyph >> Shift2) & 63)) & 1) != 0;

    public void Add(int glyph)
    {
        _mask0 |= 1UL << (glyph & 63);
        _mask1 |= 1UL << ((glyph >> Shift1) & 63);
        _mask2 |= 1UL << ((glyph >> Shift2) & 63);
    }

    /// <summary>Adds the glyphs from one glyph to another, both included; nothing when the range is empty.</summary>
    public void AddRange(int first, int last)
    {
        if (first > last)
            return;

        _mask0 |= GetRangeMask(first, last, 0);
        _mask1 |= GetRangeMask(first, last, Shift1);
        _mask2 |= GetRangeMask(first, last, Shift2);
    }

    private static ulong GetRangeMask(int first, int last, int shift)
    {
        var start = first >> shift;
        var end = last >> shift;
        if (end - start >= 63)
            return ulong.MaxValue;

        var mask = 0UL;
        for (var i = start; i <= end; i++)
        {
            mask |= 1UL << (i & 63);
        }

        return mask;
    }
}
