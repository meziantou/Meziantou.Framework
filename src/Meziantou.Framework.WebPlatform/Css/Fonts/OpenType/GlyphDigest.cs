using System.Runtime.InteropServices;

namespace Meziantou.Framework.WebPlatform.Css.Fonts;

/// <summary>
/// A compact approximation of a set of glyphs (a Bloom filter of three bit masks), which tells quickly that a lookup does
/// not apply to a glyph or to a buffer: <see cref="MayHave"/> may return true for a glyph that is not in the set, never
/// false for a glyph that is. Ported from HarfBuzz's <c>hb_set_digest_t</c> (https://github.com/harfbuzz/harfbuzz/blob/main/src/hb-set-digest.hh, MIT license).
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal struct GlyphDigest
{
    private const int Shift0 = 4;
    private const int Shift1 = 0;
    private const int Shift2 = 9;

    private ulong _mask0;
    private ulong _mask1;
    private ulong _mask2;

    /// <summary>A digest that contains every glyph.</summary>
    public static GlyphDigest Full => new() { _mask0 = ulong.MaxValue, _mask1 = ulong.MaxValue, _mask2 = ulong.MaxValue };

    /// <summary>Whether the digest is empty.</summary>
    public readonly bool IsEmpty => _mask0 == 0;

    public void Add(int glyph)
    {
        _mask0 |= Bit(glyph, Shift0);
        _mask1 |= Bit(glyph, Shift1);
        _mask2 |= Bit(glyph, Shift2);
    }

    /// <summary>Adds the glyphs from <paramref name="first"/> to <paramref name="last"/>, included.</summary>
    public void AddRange(int first, int last)
    {
        if (last < first)
            return;

        _mask0 |= RangeBits(first, last, Shift0);
        _mask1 |= RangeBits(first, last, Shift1);
        _mask2 |= RangeBits(first, last, Shift2);
    }

    public void Add(in GlyphDigest other)
    {
        _mask0 |= other._mask0;
        _mask1 |= other._mask1;
        _mask2 |= other._mask2;
    }

    public readonly bool MayHave(int glyph) => (_mask0 & Bit(glyph, Shift0)) != 0 && (_mask1 & Bit(glyph, Shift1)) != 0 && (_mask2 & Bit(glyph, Shift2)) != 0;

    public readonly bool MayIntersect(in GlyphDigest other) => (_mask0 & other._mask0) != 0 && (_mask1 & other._mask1) != 0 && (_mask2 & other._mask2) != 0;

    private static ulong Bit(int glyph, int shift) => 1UL << ((glyph >> shift) & 63);

    private static ulong RangeBits(int first, int last, int shift)
    {
        var a = first >> shift;
        var b = last >> shift;
        if (b - a >= 63)
            return ulong.MaxValue;

        // The bits from a to b, wrapping around
        var start = 1UL << (a & 63);
        var end = 1UL << (b & 63);
        return unchecked(end + (end - start) - (end < start ? 1UL : 0UL));
    }
}
