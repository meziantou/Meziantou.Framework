using System.IO.Hashing;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.BloomFilters;

public sealed partial class BloomFilterXXHash64 : BloomFilter
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Hash64 Hash(ReadOnlySpan<byte> value)
    {
        return new(XxHash64.HashToUInt64(value));
    }
}
