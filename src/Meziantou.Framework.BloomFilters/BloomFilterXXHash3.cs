using System.IO.Hashing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.BloomFilters;

public sealed partial class BloomFilterXXHash3 : BloomFilter
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Hash64 Hash<T>(in T value) where T : unmanaged
    {
        var bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(in value), 1));
        return new(XxHash3.HashToUInt64(bytes));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Hash64 Hash(string value)
    {
        var chars = value.AsSpan();
        var bytes = MemoryMarshal.AsBytes(chars);
        return new(XxHash3.HashToUInt64(bytes));
    }
}
