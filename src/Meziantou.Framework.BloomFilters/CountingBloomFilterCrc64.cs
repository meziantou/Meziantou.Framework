using System.IO.Hashing;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.BloomFilters;

public sealed partial class CountingBloomFilterCrc64 : CountingBloomFilter
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Hash64 Hash(ReadOnlySpan<byte> value)
    {
        return new(Crc64.HashToUInt64(value));
    }
}
