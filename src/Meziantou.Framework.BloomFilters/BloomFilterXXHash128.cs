using System.IO.Hashing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.BloomFilters;

public sealed partial class BloomFilterXXHash128 : BloomFilter
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Hash128 Hash<T>(in T value) where T : unmanaged
    {
        var bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(in value), 1));
        return new(XxHash128.HashToUInt128(bytes));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Hash128 Hash(string value)
    {
        var chars = value.AsSpan();
        var bytes = MemoryMarshal.AsBytes(chars);
        return new(XxHash128.HashToUInt128(bytes));
    }
}
