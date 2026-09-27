using System.IO.Hashing;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.BloomFilters;

public sealed partial class CountingBloomFilterCrc32 : CountingBloomFilter
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static BloomFilterHash Hash(ReadOnlySpan<byte> value)
    {
        return BloomFilterHash.FromUInt32(Crc32.HashToUInt32(value));
    }
}
