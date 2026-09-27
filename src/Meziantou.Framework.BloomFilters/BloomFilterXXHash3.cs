using System.IO.Hashing;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.BloomFilters;

public sealed partial class BloomFilterXXHash3 : BloomFilter
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static BloomFilterHash Hash(ReadOnlySpan<byte> value)
    {
        return BloomFilterHash.FromUInt64(XxHash3.HashToUInt64(value));
    }
}
