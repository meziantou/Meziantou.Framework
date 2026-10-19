using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The vector operations of the AV1 kernels that the cross-platform <see cref="Vector128"/> API does not express: the
/// AdvSimd instruction on Arm64, the SSE2, SSSE3 or SSE4.1 one on x64. Both compute the same lanes. The kernels use them
/// when <see cref="IsSupported"/> is true and run their scalar reference otherwise.
/// </summary>
internal static class Av1Simd
{
    /// <summary>Whether the operations are hardware accelerated.</summary>
    public static bool IsSupported => AdvSimd.Arm64.IsSupported || Sse41.IsSupported;

    /// <summary>The 64-bit products of the lanes 0 and 1 of <paramref name="left"/> and <paramref name="right"/>.</summary>
    // pmuldq multiplies the lanes 0 and 2: the lanes 0 and 1 (or 2 and 3) are first duplicated into the lanes 0 to 3
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<long> MultiplyWideningLower(Vector128<int> left, Vector128<int> right)
    {
        if (AdvSimd.Arm64.IsSupported)
            return AdvSimd.MultiplyWideningLower(left.GetLower(), right.GetLower());

        return Sse41.Multiply(Sse2.Shuffle(left, 0x50), Sse2.Shuffle(right, 0x50));
    }

    /// <summary>The 64-bit products of the lanes 2 and 3 of <paramref name="left"/> and <paramref name="right"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<long> MultiplyWideningUpper(Vector128<int> left, Vector128<int> right)
    {
        if (AdvSimd.Arm64.IsSupported)
            return AdvSimd.MultiplyWideningUpper(left, right);

        return Sse41.Multiply(Sse2.Shuffle(left, 0xFA), Sse2.Shuffle(right, 0xFA));
    }

    /// <summary><paramref name="addend"/> plus <see cref="MultiplyWideningLower(Vector128{int}, Vector128{int})"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<long> MultiplyWideningLowerAndAdd(Vector128<long> addend, Vector128<int> left, Vector128<int> right)
    {
        if (AdvSimd.Arm64.IsSupported)
            return AdvSimd.MultiplyWideningLowerAndAdd(addend, left.GetLower(), right.GetLower());

        return addend + MultiplyWideningLower(left, right);
    }

    /// <summary><paramref name="addend"/> plus <see cref="MultiplyWideningUpper(Vector128{int}, Vector128{int})"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<long> MultiplyWideningUpperAndAdd(Vector128<long> addend, Vector128<int> left, Vector128<int> right)
    {
        if (AdvSimd.Arm64.IsSupported)
            return AdvSimd.MultiplyWideningUpperAndAdd(addend, left, right);

        return addend + MultiplyWideningUpper(left, right);
    }

    /// <summary><paramref name="minuend"/> minus <see cref="MultiplyWideningLower(Vector128{int}, Vector128{int})"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<long> MultiplyWideningLowerAndSubtract(Vector128<long> minuend, Vector128<int> left, Vector128<int> right)
    {
        if (AdvSimd.Arm64.IsSupported)
            return AdvSimd.MultiplyWideningLowerAndSubtract(minuend, left.GetLower(), right.GetLower());

        return minuend - MultiplyWideningLower(left, right);
    }

    /// <summary><paramref name="minuend"/> minus <see cref="MultiplyWideningUpper(Vector128{int}, Vector128{int})"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<long> MultiplyWideningUpperAndSubtract(Vector128<long> minuend, Vector128<int> left, Vector128<int> right)
    {
        if (AdvSimd.Arm64.IsSupported)
            return AdvSimd.MultiplyWideningUpperAndSubtract(minuend, left, right);

        return minuend - MultiplyWideningUpper(left, right);
    }

    /// <summary>
    /// <c>(int)Round2( x, 12 )</c> of the lanes of <paramref name="low"/> (lanes 0 and 1 of the result) and of
    /// <paramref name="high"/> (lanes 2 and 3), for values whose rounding cannot overflow.
    /// </summary>
    // The 32 bits that the narrowing keeps are the bits 12 to 43 of the rounded value: a logical shift gives them too, and
    // x64 has no arithmetic shift of 64-bit lanes before AVX-512
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<int> RoundNarrow12(Vector128<long> low, Vector128<long> high)
    {
        if (AdvSimd.Arm64.IsSupported)
            return AdvSimd.ExtractNarrowingUpper(AdvSimd.ExtractNarrowingLower(AdvSimd.ShiftRightArithmeticRounded(low, 12)), AdvSimd.ShiftRightArithmeticRounded(high, 12));

        var rounding = Vector128.Create(1L << 11);
        return Vector128.Narrow(Vector128.ShiftRightLogical(low + rounding, 12), Vector128.ShiftRightLogical(high + rounding, 12));
    }

    /// <summary><c>Round2( x, shift )</c> of the lanes, for a shift of at least 1.</summary>
    // Without a rounding shift: the shifted value plus the last bit that the shift drops, which cannot overflow
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<int> ShiftRightArithmeticRounded(Vector128<int> value, int shift)
    {
        if (AdvSimd.Arm64.IsSupported)
        {
            switch (shift)
            {
                case 1:
                    return AdvSimd.ShiftRightArithmeticRounded(value, 1);
                case 2:
                    return AdvSimd.ShiftRightArithmeticRounded(value, 2);
                case 4:
                    return AdvSimd.ShiftRightArithmeticRounded(value, 4);
            }
        }

        return (value >> shift) + ((value >> (shift - 1)) & Vector128<int>.One);
    }

    /// <summary>The sums of the lanes, saturated to the range of <see cref="int"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<int> AddSaturate(Vector128<int> left, Vector128<int> right)
    {
        if (AdvSimd.Arm64.IsSupported)
            return AdvSimd.AddSaturate(left, right);

        return Vector128.AddSaturate(left, right);
    }

    /// <summary>The differences of the lanes, saturated to the range of <see cref="int"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<int> SubtractSaturate(Vector128<int> left, Vector128<int> right)
    {
        if (AdvSimd.Arm64.IsSupported)
            return AdvSimd.SubtractSaturate(left, right);

        return Vector128.SubtractSaturate(left, right);
    }

    /// <summary>A 4x4 transposition: the lane i of the output j is the lane j of the input i.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Transpose(Vector128<int> v0, Vector128<int> v1, Vector128<int> v2, Vector128<int> v3, out Vector128<int> r0, out Vector128<int> r1, out Vector128<int> r2, out Vector128<int> r3)
    {
        if (AdvSimd.Arm64.IsSupported)
        {
            var t0 = AdvSimd.Arm64.TransposeEven(v0, v1).AsInt64();
            var t1 = AdvSimd.Arm64.TransposeOdd(v0, v1).AsInt64();
            var t2 = AdvSimd.Arm64.TransposeEven(v2, v3).AsInt64();
            var t3 = AdvSimd.Arm64.TransposeOdd(v2, v3).AsInt64();
            r0 = AdvSimd.Arm64.TransposeEven(t0, t2).AsInt32();
            r1 = AdvSimd.Arm64.TransposeEven(t1, t3).AsInt32();
            r2 = AdvSimd.Arm64.TransposeOdd(t0, t2).AsInt32();
            r3 = AdvSimd.Arm64.TransposeOdd(t1, t3).AsInt32();
        }
        else
        {
            var t0 = Sse2.UnpackLow(v0, v1).AsInt64();
            var t1 = Sse2.UnpackLow(v2, v3).AsInt64();
            var t2 = Sse2.UnpackHigh(v0, v1).AsInt64();
            var t3 = Sse2.UnpackHigh(v2, v3).AsInt64();
            r0 = Sse2.UnpackLow(t0, t1).AsInt32();
            r1 = Sse2.UnpackHigh(t0, t1).AsInt32();
            r2 = Sse2.UnpackLow(t2, t3).AsInt32();
            r3 = Sse2.UnpackHigh(t2, t3).AsInt32();
        }
    }

    /// <summary>The 32-bit products of the lanes 0 to 3 of <paramref name="left"/> and <paramref name="right"/>.</summary>
    // pmullw and pmulhw give the low and the high 16 bits of the products, which the unpacking joins
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<int> MultiplyWideningLower(Vector128<short> left, Vector128<short> right)
    {
        if (AdvSimd.Arm64.IsSupported)
            return AdvSimd.MultiplyWideningLower(left.GetLower(), right.GetLower());

        return Sse2.UnpackLow(Sse2.MultiplyLow(left, right), Sse2.MultiplyHigh(left, right)).AsInt32();
    }

    /// <summary>The 32-bit products of the lanes 4 to 7 of <paramref name="left"/> and <paramref name="right"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<int> MultiplyWideningUpper(Vector128<short> left, Vector128<short> right)
    {
        if (AdvSimd.Arm64.IsSupported)
            return AdvSimd.MultiplyWideningUpper(left, right);

        return Sse2.UnpackHigh(Sse2.MultiplyLow(left, right), Sse2.MultiplyHigh(left, right)).AsInt32();
    }

    /// <summary>The sum of the lanes, wrapped to 16 bits.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static short AddAcross(Vector128<short> value)
    {
        if (AdvSimd.Arm64.IsSupported)
            return AdvSimd.Arm64.AddAcross(value).ToScalar();

        return Vector128.Sum(value);
    }

    /// <summary>The sum of the lanes, wrapped to 32 bits.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int AddAcross(Vector128<int> value)
    {
        if (AdvSimd.Arm64.IsSupported)
            return AdvSimd.Arm64.AddAcross(value).ToScalar();

        return Vector128.Sum(value);
    }

    /// <summary>The sums of the pairs of lanes 2i and 2i + 1 in the lanes 0 to 3, wrapped to 16 bits, and zeros in the lanes 4 to 7.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<short> AddPairwise(Vector128<short> value)
    {
        if (AdvSimd.Arm64.IsSupported)
            return AdvSimd.Arm64.AddPairwise(value, Vector128<short>.Zero);

        return Ssse3.HorizontalAdd(value, Vector128<short>.Zero);
    }

    /// <summary>
    /// The sums of the pairs of lanes 2i and 2i + 1 in the lanes 0 to 3, for lanes of at most 32767 (the sums of two
    /// samples of at most 12 bits).
    /// </summary>
    // pmaddwd multiplies the signed 16-bit lanes by ones and adds the pairs of products: the lanes are not negative as
    // signed values, so the products are the lanes
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<int> AddPairwiseWidening(Vector128<ushort> value)
    {
        if (AdvSimd.Arm64.IsSupported)
            return AdvSimd.AddPairwiseWidening(value).AsInt32();

        return Sse2.MultiplyAddAdjacent(value.AsInt16(), Vector128<short>.One);
    }

    /// <summary>
    /// The lanes <paramref name="index"/> to 7 of <paramref name="upper"/>, then the lanes 0 to <paramref name="index"/> - 1
    /// of <paramref name="lower"/> (the operand order of <see cref="AdvSimd.ExtractVector128(Vector128{short}, Vector128{short}, byte)"/>).
    /// </summary>
    // palignr shifts the concatenation of its operands right by a number of bytes, an immediate
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<short> ExtractVector128(Vector128<short> upper, Vector128<short> lower, [ConstantExpected(Max = (byte)7)] byte index)
    {
        if (AdvSimd.Arm64.IsSupported)
            return AdvSimd.ExtractVector128(upper, lower, index);

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

    /// <summary>
    /// Loads 8 consecutive samples of 4 rows, from <paramref name="first"/>, as 8 vectors of 4 lanes (one per row):
    /// the lane i of <c>columns[j]</c> is the sample <c>first + (i * lineStep) + j</c>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void LoadTransposed(ReadOnlySpan<ushort> samples, int first, int lineStep, Span<Vector128<int>> columns)
    {
        var r0 = Vector128.Create(samples.Slice(first, 8));
        var r1 = Vector128.Create(samples.Slice(first + lineStep, 8));
        var r2 = Vector128.Create(samples.Slice(first + (2 * lineStep), 8));
        var r3 = Vector128.Create(samples.Slice(first + (3 * lineStep), 8));
        if (AdvSimd.Arm64.IsSupported)
        {
            var t0 = AdvSimd.Arm64.TransposeEven(r0, r1).AsUInt32();
            var t1 = AdvSimd.Arm64.TransposeOdd(r0, r1).AsUInt32();
            var t2 = AdvSimd.Arm64.TransposeEven(r2, r3).AsUInt32();
            var t3 = AdvSimd.Arm64.TransposeOdd(r2, r3).AsUInt32();

            // Columns 0 and 4, 2 and 6, 1 and 5, 3 and 7
            var u0 = AdvSimd.Arm64.TransposeEven(t0, t2).AsUInt16();
            var u1 = AdvSimd.Arm64.TransposeOdd(t0, t2).AsUInt16();
            var u2 = AdvSimd.Arm64.TransposeEven(t1, t3).AsUInt16();
            var u3 = AdvSimd.Arm64.TransposeOdd(t1, t3).AsUInt16();
            columns[0] = Vector128.WidenLower(u0).AsInt32();
            columns[4] = Vector128.WidenUpper(u0).AsInt32();
            columns[2] = Vector128.WidenLower(u1).AsInt32();
            columns[6] = Vector128.WidenUpper(u1).AsInt32();
            columns[1] = Vector128.WidenLower(u2).AsInt32();
            columns[5] = Vector128.WidenUpper(u2).AsInt32();
            columns[3] = Vector128.WidenLower(u3).AsInt32();
            columns[7] = Vector128.WidenUpper(u3).AsInt32();
            return;
        }

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

    /// <summary>The inverse of <see cref="LoadTransposed"/>: the lanes are truncated to 16 bits.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void StoreTransposed(Span<ushort> samples, int first, int lineStep, ReadOnlySpan<Vector128<int>> columns)
    {
        if (AdvSimd.Arm64.IsSupported)
        {
            var u0 = Vector128.Narrow(columns[0].AsUInt32(), columns[4].AsUInt32()).AsUInt32();
            var u1 = Vector128.Narrow(columns[2].AsUInt32(), columns[6].AsUInt32()).AsUInt32();
            var u2 = Vector128.Narrow(columns[1].AsUInt32(), columns[5].AsUInt32()).AsUInt32();
            var u3 = Vector128.Narrow(columns[3].AsUInt32(), columns[7].AsUInt32()).AsUInt32();
            var t0 = AdvSimd.Arm64.TransposeEven(u0, u1).AsUInt16();
            var t2 = AdvSimd.Arm64.TransposeOdd(u0, u1).AsUInt16();
            var t1 = AdvSimd.Arm64.TransposeEven(u2, u3).AsUInt16();
            var t3 = AdvSimd.Arm64.TransposeOdd(u2, u3).AsUInt16();
            AdvSimd.Arm64.TransposeEven(t0, t1).CopyTo(samples.Slice(first, 8));
            AdvSimd.Arm64.TransposeOdd(t0, t1).CopyTo(samples.Slice(first + lineStep, 8));
            AdvSimd.Arm64.TransposeEven(t2, t3).CopyTo(samples.Slice(first + (2 * lineStep), 8));
            AdvSimd.Arm64.TransposeOdd(t2, t3).CopyTo(samples.Slice(first + (3 * lineStep), 8));
            return;
        }

        Transpose(columns[0], columns[1], columns[2], columns[3], out var low0, out var low1, out var low2, out var low3);
        Transpose(columns[4], columns[5], columns[6], columns[7], out var high0, out var high1, out var high2, out var high3);
        Vector128.Narrow(low0.AsUInt32(), high0.AsUInt32()).CopyTo(samples.Slice(first, 8));
        Vector128.Narrow(low1.AsUInt32(), high1.AsUInt32()).CopyTo(samples.Slice(first + lineStep, 8));
        Vector128.Narrow(low2.AsUInt32(), high2.AsUInt32()).CopyTo(samples.Slice(first + (2 * lineStep), 8));
        Vector128.Narrow(low3.AsUInt32(), high3.AsUInt32()).CopyTo(samples.Slice(first + (3 * lineStep), 8));
    }
}
