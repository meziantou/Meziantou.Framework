using System.IO.Hashing;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.BloomFilters;

public sealed partial class BloomFilterXXHash128 : BloomFilter
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Hash128 Hash(ReadOnlySpan<byte> value)
    {
        return new(XxHash128.HashToUInt128(value));
    }
}
