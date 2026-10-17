using System.Diagnostics.CodeAnalysis;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The vector operations of the AV1 kernels whose best form depends on the instruction set. A kernel is written once,
/// generic over an implementation that the JIT specializes: <see cref="Av1AdvSimd"/> on Arm64 and
/// <see cref="Av1PortableSimd"/> elsewhere. Every implementation computes the same lanes.
/// </summary>
internal interface IAv1Simd
{
    /// <summary>The 64-bit products of the lanes 0 and 1 of <paramref name="left"/> and <paramref name="right"/>.</summary>
    static abstract Vector128<long> MultiplyWideningLower(Vector128<int> left, Vector128<int> right);

    /// <summary>The 64-bit products of the lanes 2 and 3 of <paramref name="left"/> and <paramref name="right"/>.</summary>
    static abstract Vector128<long> MultiplyWideningUpper(Vector128<int> left, Vector128<int> right);

    /// <summary><paramref name="addend"/> plus <see cref="MultiplyWideningLower(Vector128{int}, Vector128{int})"/>.</summary>
    static abstract Vector128<long> MultiplyWideningLowerAndAdd(Vector128<long> addend, Vector128<int> left, Vector128<int> right);

    /// <summary><paramref name="addend"/> plus <see cref="MultiplyWideningUpper(Vector128{int}, Vector128{int})"/>.</summary>
    static abstract Vector128<long> MultiplyWideningUpperAndAdd(Vector128<long> addend, Vector128<int> left, Vector128<int> right);

    /// <summary><paramref name="minuend"/> minus <see cref="MultiplyWideningLower(Vector128{int}, Vector128{int})"/>.</summary>
    static abstract Vector128<long> MultiplyWideningLowerAndSubtract(Vector128<long> minuend, Vector128<int> left, Vector128<int> right);

    /// <summary><paramref name="minuend"/> minus <see cref="MultiplyWideningUpper(Vector128{int}, Vector128{int})"/>.</summary>
    static abstract Vector128<long> MultiplyWideningUpperAndSubtract(Vector128<long> minuend, Vector128<int> left, Vector128<int> right);

    /// <summary>
    /// <c>(int)Round2( x, 12 )</c> of the lanes of <paramref name="low"/> (lanes 0 and 1 of the result) and of
    /// <paramref name="high"/> (lanes 2 and 3), for values whose rounding cannot overflow.
    /// </summary>
    static abstract Vector128<int> RoundNarrow12(Vector128<long> low, Vector128<long> high);

    /// <summary><c>Round2( x, shift )</c> of the lanes, for a shift of at least 1.</summary>
    static abstract Vector128<int> ShiftRightArithmeticRounded(Vector128<int> value, int shift);

    /// <summary>The sums of the lanes, saturated to the range of <see cref="int"/>.</summary>
    static abstract Vector128<int> AddSaturate(Vector128<int> left, Vector128<int> right);

    /// <summary>The differences of the lanes, saturated to the range of <see cref="int"/>.</summary>
    static abstract Vector128<int> SubtractSaturate(Vector128<int> left, Vector128<int> right);

    /// <summary>A 4x4 transposition: the lane i of the output j is the lane j of the input i.</summary>
    static abstract void Transpose(Vector128<int> v0, Vector128<int> v1, Vector128<int> v2, Vector128<int> v3, out Vector128<int> r0, out Vector128<int> r1, out Vector128<int> r2, out Vector128<int> r3);

    /// <summary>The 32-bit products of the lanes 0 to 3 of <paramref name="left"/> and <paramref name="right"/>.</summary>
    static abstract Vector128<int> MultiplyWideningLower(Vector128<short> left, Vector128<short> right);

    /// <summary>The 32-bit products of the lanes 4 to 7 of <paramref name="left"/> and <paramref name="right"/>.</summary>
    static abstract Vector128<int> MultiplyWideningUpper(Vector128<short> left, Vector128<short> right);

    /// <summary>The sum of the lanes, wrapped to 16 bits.</summary>
    static abstract short AddAcross(Vector128<short> value);

    /// <summary>The sum of the lanes, wrapped to 32 bits.</summary>
    static abstract int AddAcross(Vector128<int> value);

    /// <summary>The sums of the pairs of lanes 2i and 2i + 1 in the lanes 0 to 3, wrapped to 16 bits, and zeros in the lanes 4 to 7.</summary>
    static abstract Vector128<short> AddPairwise(Vector128<short> value);

    /// <summary>
    /// The lanes <paramref name="index"/> to 7 of <paramref name="upper"/>, then the lanes 0 to <paramref name="index"/> - 1
    /// of <paramref name="lower"/> (the operand order of <see cref="AdvSimd.ExtractVector128(Vector128{short}, Vector128{short}, byte)"/>).
    /// </summary>
    static abstract Vector128<short> ExtractVector128(Vector128<short> upper, Vector128<short> lower, [ConstantExpected(Max = (byte)7)] byte index);

    /// <summary>
    /// Loads 8 consecutive samples of 4 rows, from <paramref name="first"/>, as 8 vectors of 4 lanes (one per row):
    /// the lane i of <c>columns[j]</c> is the sample <c>first + (i * lineStep) + j</c>.
    /// </summary>
    static abstract void LoadTransposed(ReadOnlySpan<ushort> samples, int first, int lineStep, Span<Vector128<int>> columns);

    /// <summary>The inverse of <see cref="LoadTransposed"/>: the lanes are truncated to 16 bits.</summary>
    static abstract void StoreTransposed(Span<ushort> samples, int first, int lineStep, ReadOnlySpan<Vector128<int>> columns);
}
