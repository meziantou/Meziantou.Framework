using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The operations of <see cref="IAv1Simd"/> with the cross-platform <see cref="Vector128"/> API, and the SSE2, SSSE3 or
/// SSE4.1 instruction that x64 has for an operation that the API does not express (widening multiplications, the
/// transpositions, the lane extractions). Without these instructions, the <see cref="Vector128"/> form computes the same
/// lanes: it is the form that the tests check on Arm64.
/// </summary>
internal readonly struct Av1PortableSimd : IAv1Simd
{
    /// <summary>Whether the kernels that multiply into 64 bits are faster than their scalar reference with this implementation.</summary>
    public static bool IsAccelerated => Sse41.IsSupported;

    // pmuldq multiplies the lanes 0 and 2: the lanes 0 and 1 (or 2 and 3) are first duplicated into the lanes 0 to 3
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<long> MultiplyWideningLower(Vector128<int> left, Vector128<int> right)
    {
        if (Sse41.IsSupported)
            return Sse41.Multiply(Sse2.Shuffle(left, 0x50), Sse2.Shuffle(right, 0x50));

        return Vector128.WidenLower(left) * Vector128.WidenLower(right);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<long> MultiplyWideningUpper(Vector128<int> left, Vector128<int> right)
    {
        if (Sse41.IsSupported)
            return Sse41.Multiply(Sse2.Shuffle(left, 0xFA), Sse2.Shuffle(right, 0xFA));

        return Vector128.WidenUpper(left) * Vector128.WidenUpper(right);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<long> MultiplyWideningLowerAndAdd(Vector128<long> addend, Vector128<int> left, Vector128<int> right) => addend + MultiplyWideningLower(left, right);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<long> MultiplyWideningUpperAndAdd(Vector128<long> addend, Vector128<int> left, Vector128<int> right) => addend + MultiplyWideningUpper(left, right);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<long> MultiplyWideningLowerAndSubtract(Vector128<long> minuend, Vector128<int> left, Vector128<int> right) => minuend - MultiplyWideningLower(left, right);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<long> MultiplyWideningUpperAndSubtract(Vector128<long> minuend, Vector128<int> left, Vector128<int> right) => minuend - MultiplyWideningUpper(left, right);

    // The 32 bits that the narrowing keeps are the bits 12 to 43 of the rounded value: a logical shift gives them too, and
    // x64 has no arithmetic shift of 64-bit lanes before AVX-512
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<int> RoundNarrow12(Vector128<long> low, Vector128<long> high)
    {
        var rounding = Vector128.Create(1L << 11);
        return Vector128.Narrow(Vector128.ShiftRightLogical(low + rounding, 12), Vector128.ShiftRightLogical(high + rounding, 12));
    }

    // Round2 without overflow: the shifted value plus the last bit that the shift drops
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<int> ShiftRightArithmeticRounded(Vector128<int> value, int shift) => (value >> shift) + ((value >> (shift - 1)) & Vector128<int>.One);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<int> AddSaturate(Vector128<int> left, Vector128<int> right) => Vector128.AddSaturate(left, right);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<int> SubtractSaturate(Vector128<int> left, Vector128<int> right) => Vector128.SubtractSaturate(left, right);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Transpose(Vector128<int> v0, Vector128<int> v1, Vector128<int> v2, Vector128<int> v3, out Vector128<int> r0, out Vector128<int> r1, out Vector128<int> r2, out Vector128<int> r3)
    {
        if (Sse2.IsSupported)
        {
            var t0 = Sse2.UnpackLow(v0, v1).AsInt64();
            var t1 = Sse2.UnpackLow(v2, v3).AsInt64();
            var t2 = Sse2.UnpackHigh(v0, v1).AsInt64();
            var t3 = Sse2.UnpackHigh(v2, v3).AsInt64();
            r0 = Sse2.UnpackLow(t0, t1).AsInt32();
            r1 = Sse2.UnpackHigh(t0, t1).AsInt32();
            r2 = Sse2.UnpackLow(t2, t3).AsInt32();
            r3 = Sse2.UnpackHigh(t2, t3).AsInt32();
            return;
        }

        r0 = Vector128.Create(v0.GetElement(0), v1.GetElement(0), v2.GetElement(0), v3.GetElement(0));
        r1 = Vector128.Create(v0.GetElement(1), v1.GetElement(1), v2.GetElement(1), v3.GetElement(1));
        r2 = Vector128.Create(v0.GetElement(2), v1.GetElement(2), v2.GetElement(2), v3.GetElement(2));
        r3 = Vector128.Create(v0.GetElement(3), v1.GetElement(3), v2.GetElement(3), v3.GetElement(3));
    }

    // pmullw and pmulhw give the low and the high 16 bits of the products, which the unpacking joins
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<int> MultiplyWideningLower(Vector128<short> left, Vector128<short> right)
    {
        if (Sse2.IsSupported)
            return Sse2.UnpackLow(Sse2.MultiplyLow(left, right), Sse2.MultiplyHigh(left, right)).AsInt32();

        return Vector128.WidenLower(left) * Vector128.WidenLower(right);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<int> MultiplyWideningUpper(Vector128<short> left, Vector128<short> right)
    {
        if (Sse2.IsSupported)
            return Sse2.UnpackHigh(Sse2.MultiplyLow(left, right), Sse2.MultiplyHigh(left, right)).AsInt32();

        return Vector128.WidenUpper(left) * Vector128.WidenUpper(right);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static short AddAcross(Vector128<short> value) => Vector128.Sum(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int AddAcross(Vector128<int> value) => Vector128.Sum(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<short> AddPairwise(Vector128<short> value)
    {
        if (Ssse3.IsSupported)
            return Ssse3.HorizontalAdd(value, Vector128<short>.Zero);

        // The lanes 2i and 2i + 1 are the low and the high halves of the 32-bit lane i
        var pairs = value.AsInt32();
        return Vector128.Narrow(((pairs << 16) >> 16) + (pairs >> 16), Vector128<int>.Zero);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<short> ExtractVector128(Vector128<short> upper, Vector128<short> lower, [ConstantExpected(Max = (byte)7)] byte index)
    {
        // palignr shifts the concatenation of its operands right by a number of bytes, an immediate
        if (Ssse3.IsSupported)
        {
            return index switch
            {
                0 => upper,
                1 => Ssse3.AlignRight(lower, upper, 2),
                2 => Ssse3.AlignRight(lower, upper, 4),
                3 => Ssse3.AlignRight(lower, upper, 6),
                4 => Ssse3.AlignRight(lower, upper, 8),
                5 => Ssse3.AlignRight(lower, upper, 10),
                6 => Ssse3.AlignRight(lower, upper, 12),
                _ => Ssse3.AlignRight(lower, upper, 14),
            };
        }

        // The lanes whose index is out of range are zero
        var indices = Vector128.Create((short)index) + Vector128.Create((short)0, 1, 2, 3, 4, 5, 6, 7);
        return Vector128.Shuffle(upper, indices) | Vector128.Shuffle(lower, indices - Vector128.Create((short)8));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void LoadTransposed(ReadOnlySpan<ushort> samples, int first, int lineStep, Span<Vector128<int>> columns)
    {
        var r0 = Vector128.Create(samples.Slice(first, 8));
        var r1 = Vector128.Create(samples.Slice(first + lineStep, 8));
        var r2 = Vector128.Create(samples.Slice(first + (2 * lineStep), 8));
        var r3 = Vector128.Create(samples.Slice(first + (3 * lineStep), 8));
        Transpose(
            Vector128.WidenLower(r0).AsInt32(),
            Vector128.WidenLower(r1).AsInt32(),
            Vector128.WidenLower(r2).AsInt32(),
            Vector128.WidenLower(r3).AsInt32(),
            out columns[0],
            out columns[1],
            out columns[2],
            out columns[3]);
        Transpose(
            Vector128.WidenUpper(r0).AsInt32(),
            Vector128.WidenUpper(r1).AsInt32(),
            Vector128.WidenUpper(r2).AsInt32(),
            Vector128.WidenUpper(r3).AsInt32(),
            out columns[4],
            out columns[5],
            out columns[6],
            out columns[7]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void StoreTransposed(Span<ushort> samples, int first, int lineStep, ReadOnlySpan<Vector128<int>> columns)
    {
        Transpose(columns[0], columns[1], columns[2], columns[3], out var low0, out var low1, out var low2, out var low3);
        Transpose(columns[4], columns[5], columns[6], columns[7], out var high0, out var high1, out var high2, out var high3);
        Vector128.Narrow(low0.AsUInt32(), high0.AsUInt32()).CopyTo(samples.Slice(first, 8));
        Vector128.Narrow(low1.AsUInt32(), high1.AsUInt32()).CopyTo(samples.Slice(first + lineStep, 8));
        Vector128.Narrow(low2.AsUInt32(), high2.AsUInt32()).CopyTo(samples.Slice(first + (2 * lineStep), 8));
        Vector128.Narrow(low3.AsUInt32(), high3.AsUInt32()).CopyTo(samples.Slice(first + (3 * lineStep), 8));
    }
}
