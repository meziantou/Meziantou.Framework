using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Counts the elementary operations spent to produce one glyph (outline points, components, operators), against
/// <see cref="FontResourceLimits.MaxGlyphComplexity"/>.
/// </summary>
/// <remarks>
/// A font is untrusted input: composite glyphs that refer to each other, or subroutines that call each other, can
/// describe an outline whose size is exponential in the size of the file. One counter bounds them all.
/// </remarks>
[StructLayout(LayoutKind.Auto)]
internal struct GlyphBudget
{
    private readonly long _limit;
    private long _used;

    public GlyphBudget(FontResourceLimits limits)
    {
        _limit = limits.MaxGlyphComplexity;
    }

    /// <summary>Spends operations.</summary>
    /// <exception cref="FontResourceLimitException">The glyph exceeds the limit.</exception>
    public void Charge(long count)
    {
        _used += count;
        if (_used > _limit)
            throw new FontResourceLimitException(FontResourceLimitKind.GlyphComplexity, _limit, _used);
    }
}
