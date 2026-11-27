using System.Numerics;

namespace Meziantou.Framework.Fonts;

/// <summary>
/// The digests of the subtables of a lookup: a subtable is tried at a glyph only when its digest may contain the glyph.
/// </summary>
/// <remarks>
/// A lookup can have hundreds of subtables (Noto Serif Tibetan has a lookup of more than 700), and each glyph the lookup
/// is tried at would be looked up in the digest of each of them. The subtables of a lookup that has many are also
/// indexed by the bits of their digests: for each bit of each of the three masks, the set of the subtables whose mask
/// has the bit. The subtables whose digests may contain a glyph are the intersection of the three sets of the bits of
/// the glyph, found 64 subtables at a time, in the order of the lookup: the same subtables, in the same order, as
/// testing each digest gives.
/// </remarks>
internal sealed class SubtableDigests
{
    // Below this number of subtables, testing each digest costs about as much as intersecting the sets.
    private const int MinIndexedCount = 16;

    private readonly GlyphDigest[] _digests;

    // The sets of subtables, of _wordCount words each: those of the 64 bits of the first mask, then of the second and
    // of the third.
    private readonly ulong[]? _sets;
    private readonly int _wordCount;

    public SubtableDigests(GlyphDigest[] digests)
    {
        _digests = digests;
        if (digests.Length < MinIndexedCount)
            return;

        _wordCount = (digests.Length + 63) >> 6;
        _sets = new ulong[GlyphDigest.MaskCount * 64 * _wordCount];
        for (var subtable = 0; subtable < digests.Length; subtable++)
        {
            for (var mask = 0; mask < GlyphDigest.MaskCount; mask++)
            {
                var bits = digests[subtable].GetMask(mask);
                while (bits != 0)
                {
                    var bit = BitOperations.TrailingZeroCount(bits);
                    _sets[((((mask * 64) + bit) * _wordCount) + (subtable >> 6))] |= 1UL << (subtable & 63);
                    bits &= bits - 1;
                }
            }
        }
    }

    /// <summary>Gets the number of subtables.</summary>
    public int Count => _digests.Length;

    /// <summary>Gets a value indicating whether the subtables are indexed by the bits of their digests (<see cref="GetCandidates"/>).</summary>
    public bool IsIndexed => _sets is not null;

    /// <summary>Gets the number of words of 64 subtables <see cref="GetCandidates"/> gives.</summary>
    public int WordCount => _wordCount;

    /// <summary>Determines whether the digest of a subtable may contain a glyph.</summary>
    public bool MayContain(int subtable, int glyph) => _digests[subtable].MayContain(glyph);

    /// <summary>
    /// Gets the subtables of a word whose digests may contain a glyph: bit <c>i</c> stands for the subtable
    /// <c>64 * word + i</c>. The subtables must be indexed (<see cref="IsIndexed"/>).
    /// </summary>
    public ulong GetCandidates(int word, int glyph)
    {
        var sets = _sets!;
        GlyphDigest.GetBits(glyph, out var bit0, out var bit1, out var bit2);
        return sets[(bit0 * _wordCount) + word] & sets[((64 + bit1) * _wordCount) + word] & sets[((128 + bit2) * _wordCount) + word];
    }
}
