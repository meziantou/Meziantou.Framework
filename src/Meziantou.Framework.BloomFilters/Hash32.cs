using System.Runtime.InteropServices;

namespace Meziantou.Framework.BloomFilters;

[StructLayout(LayoutKind.Auto)]
internal readonly struct Hash32
{
    public readonly uint Hash1;
    public readonly uint Hash2;

    public Hash32(uint value)
    {
        // Splitting a 32-bit hash into two 16-bit halves leaves only 16 bits of index entropy, which caps
        // the number of reachable positions at 65,536 whatever the filter size is. Expand the hash to
        // 64 bits first so both halves are full width.
        var expanded = Expand(value);
        Hash1 = (uint)expanded;
        var hash2 = (uint)(expanded >> 32);

        // The i-th position is derived as Hash1 + i * Hash2 in modular arithmetic, so an even step has a
        // cycle shorter than the word size and revisits positions. Keep the step odd for a full period.
        Hash2 = hash2 == 0 ? 0x9E3779B9U : hash2 | 1; // Golden ratio
    }

    /// <summary>
    /// Spreads a 32-bit value over 64 bits using the splitmix64 finalizer.
    /// </summary>
    private static ulong Expand(uint value)
    {
        var result = value + 0x9E3779B97F4A7C15UL;
        result = (result ^ (result >> 30)) * 0xBF58476D1CE4E5B9UL;
        result = (result ^ (result >> 27)) * 0x94D049BB133111EBUL;
        return result ^ (result >> 31);
    }
}
