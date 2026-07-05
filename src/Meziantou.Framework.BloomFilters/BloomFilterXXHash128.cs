using System.IO.Hashing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
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
        AddRangeCore(MemoryMarshal.Cast<long, ulong>(values));
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

        ref var valuesReference = ref MemoryMarshal.GetReference(values);
        var index = 0;
        if (AdvSimd.Arm64.IsSupported)
        {
            while (index <= values.Length - Vector128<ulong>.Count)
            {
                var current = Vector128.LoadUnsafe(ref valuesReference, (nuint)index);
                Multiply64To128(current ^ Vector128.Create(BitFlip), Prime1 + (sizeof(ulong) << 2), out var low, out var high);
                high += low << 1;
                low ^= high >> 3;
                low ^= low >> 35;
                low = MultiplyLow(low, RrmxmxMultiplier);
                low ^= low >> 28;
                high ^= high >> 37;
                high = MultiplyLow(high, AvalancheMultiplier);
                high ^= high >> 32;

                for (var lane = 0; lane < Vector128<ulong>.Count; lane++)
                {
                    AddHash(new Hash128(low[lane], high[lane]));
                }

                index += Vector128<ulong>.Count;
            }
        }
        else
        if (Avx2.IsSupported)
        {
            while (index <= values.Length - Vector256<ulong>.Count)
            {
                var current = Vector256.LoadUnsafe(ref valuesReference, (nuint)index);
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
                    AddHash(new Hash128(low[lane], high[lane]));
                }

                index += Vector256<ulong>.Count;
            }
        }

        foreach (var value in values[index..])
        {
            Add(value);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Hash128 Hash(ReadOnlySpan<byte> value)
    {
        return new(XxHash128.HashToUInt128(value));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<ulong> MultiplyLow(Vector128<ulong> value, ulong multiplier)
    {
        Split(value, out var lower, out var upper);
        var multiplierLower = Vector64.Create((uint)multiplier);
        var multiplierUpper = Vector64.Create((uint)(multiplier >> 32));
        var productLower = AdvSimd.MultiplyWideningLower(lower, multiplierLower);
        var productMiddle1 = AdvSimd.MultiplyWideningLower(lower, multiplierUpper);
        var productMiddle2 = AdvSimd.MultiplyWideningLower(upper, multiplierLower);
        return productLower + ((productMiddle1 + productMiddle2) << 32);
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
    private static void Multiply64To128(Vector128<ulong> value, ulong multiplier, out Vector128<ulong> low, out Vector128<ulong> high)
    {
        Split(value, out var lower, out var upper);
        var multiplierLower = Vector64.Create((uint)multiplier);
        var multiplierUpper = Vector64.Create((uint)(multiplier >> 32));
        var productLower = AdvSimd.MultiplyWideningLower(lower, multiplierLower);
        var productMiddle1 = AdvSimd.MultiplyWideningLower(lower, multiplierUpper);
        var productMiddle2 = AdvSimd.MultiplyWideningLower(upper, multiplierLower);
        var productUpper = AdvSimd.MultiplyWideningLower(upper, multiplierUpper);
        var mask = Vector128.Create(UInt32Mask);
        var middle = (productLower >> 32) + (productMiddle1 & mask) + (productMiddle2 & mask);
        low = (productLower & mask) | (middle << 32);
        high = productUpper + (productMiddle1 >> 32) + (productMiddle2 >> 32) + (middle >> 32);
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Split(Vector128<ulong> value, out Vector64<uint> lower, out Vector64<uint> upper)
    {
        var words = value.AsUInt32();
        lower = AdvSimd.Arm64.UnzipEven(words, words).GetLower();
        upper = AdvSimd.Arm64.UnzipOdd(words, words).GetLower();
    }
}
