using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The operations of <see cref="IAv1Simd"/> with the AdvSimd instructions of Arm64.</summary>
internal readonly struct Av1AdvSimd : IAv1Simd
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<long> MultiplyWideningLower(Vector128<int> left, Vector128<int> right) => AdvSimd.MultiplyWideningLower(left.GetLower(), right.GetLower());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<long> MultiplyWideningUpper(Vector128<int> left, Vector128<int> right) => AdvSimd.MultiplyWideningUpper(left, right);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<long> MultiplyWideningLowerAndAdd(Vector128<long> addend, Vector128<int> left, Vector128<int> right) => AdvSimd.MultiplyWideningLowerAndAdd(addend, left.GetLower(), right.GetLower());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<long> MultiplyWideningUpperAndAdd(Vector128<long> addend, Vector128<int> left, Vector128<int> right) => AdvSimd.MultiplyWideningUpperAndAdd(addend, left, right);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<long> MultiplyWideningLowerAndSubtract(Vector128<long> minuend, Vector128<int> left, Vector128<int> right) => AdvSimd.MultiplyWideningLowerAndSubtract(minuend, left.GetLower(), right.GetLower());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<long> MultiplyWideningUpperAndSubtract(Vector128<long> minuend, Vector128<int> left, Vector128<int> right) => AdvSimd.MultiplyWideningUpperAndSubtract(minuend, left, right);

    // A rounding shift of the 64-bit values (which cannot overflow), then a truncating narrowing
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<int> RoundNarrow12(Vector128<long> low, Vector128<long> high)
        => AdvSimd.ExtractNarrowingUpper(AdvSimd.ExtractNarrowingLower(AdvSimd.ShiftRightArithmeticRounded(low, 12)), AdvSimd.ShiftRightArithmeticRounded(high, 12));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<int> ShiftRightArithmeticRounded(Vector128<int> value, int shift) => shift switch
    {
        1 => AdvSimd.ShiftRightArithmeticRounded(value, 1),
        2 => AdvSimd.ShiftRightArithmeticRounded(value, 2),
        4 => AdvSimd.ShiftRightArithmeticRounded(value, 4),
        _ => Av1PortableSimd.ShiftRightArithmeticRounded(value, shift),
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<int> AddSaturate(Vector128<int> left, Vector128<int> right) => AdvSimd.AddSaturate(left, right);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<int> SubtractSaturate(Vector128<int> left, Vector128<int> right) => AdvSimd.SubtractSaturate(left, right);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Transpose(Vector128<int> v0, Vector128<int> v1, Vector128<int> v2, Vector128<int> v3, out Vector128<int> r0, out Vector128<int> r1, out Vector128<int> r2, out Vector128<int> r3)
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<int> MultiplyWideningLower(Vector128<short> left, Vector128<short> right) => AdvSimd.MultiplyWideningLower(left.GetLower(), right.GetLower());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<int> MultiplyWideningUpper(Vector128<short> left, Vector128<short> right) => AdvSimd.MultiplyWideningUpper(left, right);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static short AddAcross(Vector128<short> value) => AdvSimd.Arm64.AddAcross(value).ToScalar();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int AddAcross(Vector128<int> value) => AdvSimd.Arm64.AddAcross(value).ToScalar();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<short> AddPairwise(Vector128<short> value) => AdvSimd.Arm64.AddPairwise(value, Vector128<short>.Zero);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<short> ExtractVector128(Vector128<short> upper, Vector128<short> lower, [ConstantExpected(Max = (byte)7)] byte index) => AdvSimd.ExtractVector128(upper, lower, index);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void LoadTransposed(ReadOnlySpan<ushort> samples, int first, int lineStep, Span<Vector128<int>> columns)
    {
        var r0 = Vector128.Create(samples.Slice(first, 8));
        var r1 = Vector128.Create(samples.Slice(first + lineStep, 8));
        var r2 = Vector128.Create(samples.Slice(first + (2 * lineStep), 8));
        var r3 = Vector128.Create(samples.Slice(first + (3 * lineStep), 8));
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
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void StoreTransposed(Span<ushort> samples, int first, int lineStep, ReadOnlySpan<Vector128<int>> columns)
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
    }
}
