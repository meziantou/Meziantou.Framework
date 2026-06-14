using System.IO.Hashing;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.BloomFilters;

public sealed partial class CountingBloomFilterCrc32 : CountingBloomFilter
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Hash32 Hash(ReadOnlySpan<byte> value)
    {
        return new(Crc32.HashToUInt32(value));
    }
}
