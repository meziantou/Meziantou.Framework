using System.IO.Hashing;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.BloomFilters;

public sealed partial class BloomFilterXXHash32 : BloomFilter
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Hash32 Hash(ReadOnlySpan<byte> value)
    {
        return new(XxHash32.HashToUInt32(value));
    }
}
