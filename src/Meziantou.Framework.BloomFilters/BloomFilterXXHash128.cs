using System.IO.Hashing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Meziantou.Framework.BloomFilters;

public sealed partial class BloomFilterXXHash128 : BloomFilter
{
    private const ulong AvalancheMultiplier = 0x165667919E3779F9UL;
    private const ulong BitFlip = 0xC4F023344DC994ACUL;
    private const ulong Prime1 = 0x9E3779B185EBCA87UL;
    private const ulong RrmxmxMultiplier = 0x9FB21C651E98DF25UL;
    private const ulong UInt32Mask = uint.MaxValue;

    private void AddRangeCore(ReadOnlySpan<long> values)
    {
        AddRangeCore(unsafe(MemoryMarshal.Cast<long, ulong>(values)));
    }

    private void AddRangeCore(ReadOnlySpan<ulong> values)
    {
        if (!BitConverter.IsLittleEndian)
        {
            foreach (var value in values)
            {
                Add(value);
            }

            return;
        }

        var index = 0;
        // AdvSimd has no 64-bit lane multiply, so a Vector128 version must build each 64-bit product from four
        // 32-bit ones. The scalar hash, which uses the native 64-bit multiply, is faster on Arm64.
        if (Avx2.IsSupported)
        {
            while (index <= values.Length - Vector256<ulong>.Count)
            {
                var current = Vector256.Create(values[index..]);
                Multiply64To128(current ^ Vector256.Create(BitFlip), Prime1 + (sizeof(ulong) << 2), out var low, out var high);
                high += low << 1;
                low ^= high >> 3;
                low ^= low >> 35;
                low = MultiplyLow(low, RrmxmxMultiplier);
                low ^= low >> 28;
                high ^= high >> 37;
                high = MultiplyLow(high, AvalancheMultiplier);
                high ^= high >> 32;

                for (var lane = 0; lane < Vector256<ulong>.Count; lane++)
                {
                    AddHash(BloomFilterHash.FromUInt128(low[lane], high[lane]));
                }

                index += Vector256<ulong>.Count;
            }
        }

        foreach (var value in values[index..])
        {
            var high = HashUInt64(value, out var low);
            AddHash(BloomFilterHash.FromUInt128(low, high));
        }
    }

    /// <summary>Computes <see cref="XxHash128"/> of the little-endian bytes of <paramref name="value"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong HashUInt64(ulong value, out ulong low)
    {
        var high = Math.BigMul(value ^ BitFlip, Prime1 + (sizeof(ulong) << 2), out low);
        high += low << 1;
        low ^= high >> 3;
        low ^= low >> 35;
        low *= RrmxmxMultiplier;
        low ^= low >> 28;
        high ^= high >> 37;
        high *= AvalancheMultiplier;
        high ^= high >> 32;
        return high;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static BloomFilterHash Hash(ReadOnlySpan<byte> value)
    {
        return BloomFilterHash.FromUInt128(XxHash128.HashToUInt128(value));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<ulong> MultiplyLow(Vector256<ulong> value, ulong multiplier)
    {
        var lower = value.AsUInt32();
        var upper = (value >> 32).AsUInt32();
        var multiplierLower = Vector256.Create((uint)multiplier);
        var multiplierUpper = Vector256.Create((uint)(multiplier >> 32));
        var productLower = Avx2.Multiply(lower, multiplierLower);
        var productMiddle1 = Avx2.Multiply(lower, multiplierUpper);
        var productMiddle2 = Avx2.Multiply(upper, multiplierLower);
        return productLower + ((productMiddle1 + productMiddle2) << 32);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Multiply64To128(Vector256<ulong> value, ulong multiplier, out Vector256<ulong> low, out Vector256<ulong> high)
    {
        var lower = value.AsUInt32();
        var upper = (value >> 32).AsUInt32();
        var multiplierLower = Vector256.Create((uint)multiplier);
        var multiplierUpper = Vector256.Create((uint)(multiplier >> 32));
        var productLower = Avx2.Multiply(lower, multiplierLower);
        var productMiddle1 = Avx2.Multiply(lower, multiplierUpper);
        var productMiddle2 = Avx2.Multiply(upper, multiplierLower);
        var productUpper = Avx2.Multiply(upper, multiplierUpper);
        var mask = Vector256.Create(UInt32Mask);
        var middle = (productLower >> 32) + (productMiddle1 & mask) + (productMiddle2 & mask);
        low = (productLower & mask) | (middle << 32);
        high = productUpper + (productMiddle1 >> 32) + (productMiddle2 >> 32) + (middle >> 32);
    }
}
