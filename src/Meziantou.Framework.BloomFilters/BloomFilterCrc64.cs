using System.IO.Hashing;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.BloomFilters;

public sealed partial class BloomFilterCrc64 : BloomFilter
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static BloomFilterHash Hash(ReadOnlySpan<byte> value)
    {
        return BloomFilterHash.FromUInt64(Crc64.HashToUInt64(value));
    }
}
