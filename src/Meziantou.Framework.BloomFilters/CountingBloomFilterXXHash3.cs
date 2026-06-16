using System.IO.Hashing;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.BloomFilters;

public sealed partial class CountingBloomFilterXXHash3 : CountingBloomFilter
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Hash64 Hash(ReadOnlySpan<byte> value)
    {
        return new(XxHash3.HashToUInt64(value));
    }
}
