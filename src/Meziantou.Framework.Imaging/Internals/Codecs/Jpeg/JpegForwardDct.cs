using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The 8x8 forward DCT and quantization of the JPEG encoder (ITU-T T.81 sections A.3.3 and A.3.4), with the numerical
/// contract of the library:
/// <c>F(u, v) = 1/4 C(u) C(v) sum_x sum_y s(x, y) cos((2x + 1) u pi / 16) cos((2y + 1) v pi / 16)</c> with
/// <c>C(0) = 1 / sqrt(2)</c>, evaluated separably in IEEE double precision (columns first, then rows) from the level-shifted
/// samples, then <c>Sq(u, v) = round(F(u, v) / Q(u, v))</c> to nearest with ties away from zero. Each 8-point transform
/// splits the samples into the sums and differences of the pairs symmetric about the center (the basis functions are even
/// for even frequencies and odd for odd ones): 22 multiplications and 28 additions instead of 64 and 56. The coefficients
/// are within a few units in the last place of the exact transform, so the quantized values are the correctly rounded ones
/// except at exact ties. The basis values <c>C(u)/2 cos(k pi / 16)</c> are the literal constants of the inverse transform
/// (<see cref="JpegIdct"/>), so results do not depend on the platform's <c>Math.Cos</c>.
/// </summary>
/// <remarks>
/// With hardware vector support, each lane of <see cref="Vector128{T}"/> (two lanes) or of AVX2 (four lanes) transforms a
/// column, then a row, with the operations of the scalar reference (<see cref="TransformAndQuantizeScalar"/>) in the same
/// order and without fused multiply-adds (with AVX-512, the eight columns then the eight rows at once, transposed in
/// registers, and the zig-zag reordering done by permutations); the quantization divides and rounds exactly like
/// <see cref="Math.Round(double, MidpointRounding)"/> (the fraction <c>x - trunc(x)</c> is exact, or the conversion of Arm64
/// that rounds ties away from zero), so the coefficients are identical. The columns are stored transposed, so that the row
/// pass reads its lanes contiguously too, and the divisors are in the transposed order of <see cref="CreateDivisors"/>.
/// </remarks>
internal static class JpegForwardDct
{
    // C(0)/2 = 1 / (2 * sqrt(2)) = cos(4 pi / 16) / 2 and cos(k * pi / 16) / 2 for k = 1..7 (identical to JpegIdct)
    private const double C1 = 0.4903926402016152;
    private const double C2 = 0.46193976625564337;
    private const double C3 = 0.4157348061512726;
    private const double C4 = 0.35355339059327373;
    private const double C5 = 0.27778511650980114;
    private const double C6 = 0.19134171618254492;
    private const double C7 = 0.09754516100806417;

    /// <summary>Gets the largest magnitude of a quantized AC coefficient in a baseline scan (size category 10).</summary>
    public const int MaxAcMagnitude = 1023;

    /// <summary>Gets the smallest quantized DC coefficient (an all-black block at quantization value 1 is -1024).</summary>
    public const int MinDc = -1024;

    /// <summary>Gets the largest quantized DC coefficient, so that every DC difference stays within category 11 (at most 2047).</summary>
    public const int MaxDc = 1023;

    /// <summary>Gets the zig-zag position of each coefficient of the transposed order (<c>u * 8 + v</c>, for the natural index <c>v * 8 + u</c>).</summary>
    private static ReadOnlySpan<byte> TransposedZigZagPositions =>
    [
        0, 2, 3, 9, 10, 20, 21, 35,
        1, 4, 8, 11, 19, 22, 34, 36,
        5, 7, 12, 18, 23, 33, 37, 48,
        6, 13, 17, 24, 32, 38, 47, 49,
        14, 16, 25, 31, 39, 46, 50, 57,
        15, 26, 30, 40, 45, 51, 56, 58,
        27, 29, 41, 44, 52, 55, 59, 62,
        28, 42, 43, 53, 54, 60, 61, 63,
    ];

    /// <summary>Gets the transposed index (<c>u * 8 + v</c>) of the coefficient at each zig-zag position: the inverse of <see cref="TransposedZigZagPositions"/>.</summary>
    private static ReadOnlySpan<int> ZigZagTransposedIndexes =>
    [
        0, 8, 1, 2, 9, 16, 24, 17,
        10, 3, 4, 11, 18, 25, 32, 40,
        33, 26, 19, 12, 5, 6, 13, 20,
        27, 34, 41, 48, 56, 49, 42, 35,
        28, 21, 14, 7, 15, 22, 29, 36,
        43, 50, 57, 58, 51, 44, 37, 30,
        23, 31, 38, 45, 52, 59, 60, 53,
        46, 39, 47, 54, 61, 62, 55, 63,
    ];

    /// <summary>Converts a quantization table to the divisors of <see cref="TransformAndQuantize"/>.</summary>
    /// <param name="quantization">64 quantization values (natural order: <c>Q[v * 8 + u]</c>).</param>
    /// <param name="divisors">64 doubles, in the transposed order: <c>divisors[u * 8 + v] = Q[v * 8 + u]</c>.</param>
    public static void CreateDivisors(ReadOnlySpan<ushort> quantization, Span<double> divisors)
    {
        quantization = quantization[..64];
        divisors = divisors[..64];
        for (var v = 0; v < 8; v++)
        {
            for (var u = 0; u < 8; u++)
            {
                divisors[(u * 8) + v] = quantization[(v * 8) + u];
            }
        }
    }

    /// <summary>Transforms and quantizes one block.</summary>
    /// <param name="samples">64 level-shifted samples (natural order: <c>samples[y * 8 + x]</c>, nominally -128 to 127.5).</param>
    /// <param name="divisors">The 64 quantization values of <see cref="CreateDivisors"/>.</param>
    /// <param name="scratch">64 doubles of scratch space.</param>
    /// <param name="coefficients">The 64 quantized coefficients, in zig-zag order.</param>
    public static void TransformAndQuantize(ReadOnlySpan<float> samples, ReadOnlySpan<double> divisors, Span<double> scratch, Span<int> coefficients)
    {
        if (Vector512.IsHardwareAccelerated && Avx512F.IsSupported)
        {
            TransformAndQuantizeAvx512(samples, divisors, coefficients);
        }
        else if (Avx2.IsSupported)
        {
            TransformAndQuantizeAvx2(samples, divisors, scratch, coefficients);
        }
        else if (Vector128.IsHardwareAccelerated)
        {
            TransformAndQuantizeVector128(samples, divisors, scratch, coefficients);
        }
        else
        {
            TransformAndQuantizeScalar(samples, divisors, scratch, coefficients);
        }
    }

    /// <summary>The scalar reference of <see cref="TransformAndQuantize"/>.</summary>
    internal static void TransformAndQuantizeScalar(ReadOnlySpan<float> samples, ReadOnlySpan<double> divisors, Span<double> scratch, Span<int> coefficients)
    {
        samples = samples[..64];
        divisors = divisors[..64];
        coefficients = coefficients[..64];
        var columns = scratch[..64];

        // Columns, stored transposed: columns[x * 8 + v] = sum_y B[v][y] s(x, y)
        for (var x = 0; x < 8; x++)
        {
            Transform(
                samples[x], samples[8 + x], samples[16 + x], samples[24 + x], samples[32 + x], samples[40 + x], samples[48 + x], samples[56 + x],
                columns.Slice(x * 8, 8));
        }

        // Rows: F(u, v) = sum_x B[u][x] columns[x * 8 + v], then quantization (transposed index u * 8 + v)
        Span<double> line = stackalloc double[8];
        var positions = TransposedZigZagPositions;
        for (var v = 0; v < 8; v++)
        {
            Transform(columns[v], columns[8 + v], columns[16 + v], columns[24 + v], columns[32 + v], columns[40 + v], columns[48 + v], columns[56 + v], line);
            for (var u = 0; u < 8; u++)
            {
                var index = (u * 8) + v;
                var quantized = (int)Math.Round(line[u] / divisors[index], MidpointRounding.AwayFromZero);
                coefficients[positions[index]] = index == 0 ? Math.Clamp(quantized, MinDc, MaxDc) : Math.Clamp(quantized, -MaxAcMagnitude, MaxAcMagnitude);
            }
        }
    }

    // The 8-point transform X[k] = sum_i B[k][i] x[i], from the sums and differences of the symmetric pairs (the reference
    // order of the operations of every vector lane)
    private static void Transform(double x0, double x1, double x2, double x3, double x4, double x5, double x6, double x7, Span<double> output)
    {
        var s07 = x0 + x7;
        var s16 = x1 + x6;
        var s25 = x2 + x5;
        var s34 = x3 + x4;
        var d07 = x0 - x7;
        var d16 = x1 - x6;
        var d25 = x2 - x5;
        var d34 = x3 - x4;
        var a0 = s07 + s34;
        var a1 = s16 + s25;
        var a2 = s07 - s34;
        var a3 = s16 - s25;
        output[0] = (a0 + a1) * C4;
        output[4] = (a0 - a1) * C4;
        output[2] = (a2 * C2) + (a3 * C6);
        output[6] = (a2 * C6) - (a3 * C2);
        output[1] = (d07 * C1) + (d16 * C3) + (d25 * C5) + (d34 * C7);
        output[3] = (d07 * C3) - (d16 * C7) - (d25 * C1) - (d34 * C5);
        output[5] = (d07 * C5) - (d16 * C1) + (d25 * C7) + (d34 * C3);
        output[7] = (d07 * C7) - (d16 * C5) + (d25 * C3) - (d34 * C1);
    }

    /// <summary>The transform with <see cref="Vector128{T}"/>: two columns, then two rows, at once.</summary>
    internal static void TransformAndQuantizeVector128(ReadOnlySpan<float> samples, ReadOnlySpan<double> divisors, Span<double> scratch, Span<int> coefficients)
    {
        samples = samples[..64];
        divisors = divisors[..64];
        coefficients = coefficients[..64];
        ref var source = ref unsafe(MemoryMarshal.GetReference(samples));
        ref var columns = ref unsafe(MemoryMarshal.GetReference(scratch[..64]));

        // Columns x to x + 3 of the rows (4 floats), transformed two columns at a time and stored transposed: the outputs v
        // and v + 1 of a column pair are exchanged as a 2 x 2 block
        for (var x = 0; x < 8; x += 4)
        {
            var r0 = LoadSamples(ref source, x);
            var r1 = LoadSamples(ref source, 8 + x);
            var r2 = LoadSamples(ref source, 16 + x);
            var r3 = LoadSamples(ref source, 24 + x);
            var r4 = LoadSamples(ref source, 32 + x);
            var r5 = LoadSamples(ref source, 40 + x);
            var r6 = LoadSamples(ref source, 48 + x);
            var r7 = LoadSamples(ref source, 56 + x);
            TransformColumns(Vector128.WidenLower(r0), Vector128.WidenLower(r1), Vector128.WidenLower(r2), Vector128.WidenLower(r3), Vector128.WidenLower(r4), Vector128.WidenLower(r5), Vector128.WidenLower(r6), Vector128.WidenLower(r7), ref unsafe(Unsafe.Add(ref columns, x * 8)));
            TransformColumns(Vector128.WidenUpper(r0), Vector128.WidenUpper(r1), Vector128.WidenUpper(r2), Vector128.WidenUpper(r3), Vector128.WidenUpper(r4), Vector128.WidenUpper(r5), Vector128.WidenUpper(r6), Vector128.WidenUpper(r7), ref unsafe(Unsafe.Add(ref columns, (x + 2) * 8)));
        }

        // Rows v and v + 1, then quantization: the lanes of output u are the transposed indexes u * 8 + v and u * 8 + v + 1
        ref var divisor = ref unsafe(MemoryMarshal.GetReference(divisors));
        ref var destination = ref unsafe(MemoryMarshal.GetReference(coefficients));
        for (var v = 0; v < 8; v += 2)
        {
            Transform(
                Load(ref columns, v), Load(ref columns, 8 + v), Load(ref columns, 16 + v), Load(ref columns, 24 + v),
                Load(ref columns, 32 + v), Load(ref columns, 40 + v), Load(ref columns, 48 + v), Load(ref columns, 56 + v),
                out var f0, out var f1, out var f2, out var f3, out var f4, out var f5, out var f6, out var f7);
            Quantize(f0, ref divisor, ref destination, v);
            Quantize(f1, ref divisor, ref destination, 8 + v);
            Quantize(f2, ref divisor, ref destination, 16 + v);
            Quantize(f3, ref divisor, ref destination, 24 + v);
            Quantize(f4, ref divisor, ref destination, 32 + v);
            Quantize(f5, ref divisor, ref destination, 40 + v);
            Quantize(f6, ref divisor, ref destination, 48 + v);
            Quantize(f7, ref divisor, ref destination, 56 + v);
        }

        static Vector128<float> LoadSamples(ref float source, int index) => unsafe(Vector128.LoadUnsafe(ref source, (nuint)index));

        static Vector128<double> Load(ref double source, int index) => unsafe(Vector128.LoadUnsafe(ref source, (nuint)index));

        // Transforms the columns of two lanes and stores them transposed at destination (the first column) and destination + 8
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void TransformColumns(Vector128<double> x0, Vector128<double> x1, Vector128<double> x2, Vector128<double> x3, Vector128<double> x4, Vector128<double> x5, Vector128<double> x6, Vector128<double> x7, ref double destination)
        {
            Transform(x0, x1, x2, x3, x4, x5, x6, x7, out var c0, out var c1, out var c2, out var c3, out var c4, out var c5, out var c6, out var c7);
            Store(ref destination, 0, c0, c1);
            Store(ref destination, 2, c2, c3);
            Store(ref destination, 4, c4, c5);
            Store(ref destination, 6, c6, c7);
        }

        // Stores (a0, b0) at index and (a1, b1) at index + 8
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void Store(ref double destination, int index, Vector128<double> a, Vector128<double> b)
        {
            unsafe { UnpackLow(a, b).StoreUnsafe(ref destination, (nuint)index); }
            unsafe { UnpackHigh(a, b).StoreUnsafe(ref destination, (nuint)(index + 8)); }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Transform(Vector128<double> x0, Vector128<double> x1, Vector128<double> x2, Vector128<double> x3, Vector128<double> x4, Vector128<double> x5, Vector128<double> x6, Vector128<double> x7,
        out Vector128<double> y0, out Vector128<double> y1, out Vector128<double> y2, out Vector128<double> y3, out Vector128<double> y4, out Vector128<double> y5, out Vector128<double> y6, out Vector128<double> y7)
    {
        var s07 = x0 + x7;
        var s16 = x1 + x6;
        var s25 = x2 + x5;
        var s34 = x3 + x4;
        var d07 = x0 - x7;
        var d16 = x1 - x6;
        var d25 = x2 - x5;
        var d34 = x3 - x4;
        var a0 = s07 + s34;
        var a1 = s16 + s25;
        var a2 = s07 - s34;
        var a3 = s16 - s25;
        y0 = (a0 + a1) * C4;
        y4 = (a0 - a1) * C4;
        y2 = (a2 * C2) + (a3 * C6);
        y6 = (a2 * C6) - (a3 * C2);
        y1 = (d07 * C1) + (d16 * C3) + (d25 * C5) + (d34 * C7);
        y3 = (d07 * C3) - (d16 * C7) - (d25 * C1) - (d34 * C5);
        y5 = (d07 * C5) - (d16 * C1) + (d25 * C7) + (d34 * C3);
        y7 = (d07 * C7) - (d16 * C5) + (d25 * C3) - (d34 * C1);
    }

    /// <summary>Quantizes the coefficients of the transposed indexes <paramref name="index"/> and <paramref name="index"/> + 1, and writes them at their zig-zag positions.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Quantize(Vector128<double> coefficient, ref double divisors, ref int destination, int index)
    {
        // The clamping of the scalar reference, applied before rounding (the same result: the bounds are integers); the DC
        // coefficient (index 0) has its own range. The quotients are finite and the bounds are not zero, so the native
        // minimum and maximum (MINPD and MAXPD on x64, without the fix-ups for NaN and signed zeros) give the same values
        var quotient = coefficient / unsafe(Vector128.LoadUnsafe(ref divisors, (nuint)index));
        var minimum = index == 0 ? Vector128.Create((double)MinDc, -MaxAcMagnitude) : Vector128.Create((double)-MaxAcMagnitude);
        var maximum = index == 0 ? Vector128.Create((double)MaxDc, MaxAcMagnitude) : Vector128.Create((double)MaxAcMagnitude);
        var clamped = Vector128.MinNative(Vector128.MaxNative(quotient, minimum), maximum);
        ref var positions = ref unsafe(Unsafe.Add(ref MemoryMarshal.GetReference(TransposedZigZagPositions), index));
        if (AdvSimd.Arm64.IsSupported)
        {
            // FCVTAS: to nearest, ties away from zero
            var integers = AdvSimd.Arm64.ConvertToInt64RoundAwayFromZero(clamped);
            unsafe { Unsafe.Add(ref destination, positions) = (int)integers.ToScalar(); }
            unsafe { Unsafe.Add(ref destination, Unsafe.Add(ref positions, 1)) = (int)integers.GetElement(1); }
            return;
        }

        var rounded = RoundAwayFromZero(clamped);
        if (Sse2.IsSupported)
        {
            // x64 has no 64-bit integer conversion before AVX-512: CVTTPD2DQ, exact on the clamped integral values
            var converted = Sse2.ConvertToVector128Int32WithTruncation(rounded);
            unsafe { Unsafe.Add(ref destination, positions) = converted.ToScalar(); }
            unsafe { Unsafe.Add(ref destination, Unsafe.Add(ref positions, 1)) = converted.GetElement(1); }
            return;
        }

        var converted64 = Vector128.ConvertToInt64(rounded);
        unsafe { Unsafe.Add(ref destination, positions) = (int)converted64.ToScalar(); }
        unsafe { Unsafe.Add(ref destination, Unsafe.Add(ref positions, 1)) = (int)converted64.GetElement(1); }

        // trunc(q) plus or minus 1 when the exact fraction reaches one half
        static Vector128<double> RoundAwayFromZero(Vector128<double> value)
        {
            var half = Vector128.Create(0.5);
            var truncated = Vector128.Truncate(value);
            var fraction = value - truncated;
            return truncated
                + Vector128.ConditionalSelect(Vector128.GreaterThanOrEqual(fraction, half), Vector128<double>.One, Vector128<double>.Zero)
                - Vector128.ConditionalSelect(Vector128.LessThanOrEqual(fraction, -half), Vector128<double>.One, Vector128<double>.Zero);
        }
    }

    /// <summary>(a0, b0).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<double> UnpackLow(Vector128<double> a, Vector128<double> b)
    {
        if (AdvSimd.Arm64.IsSupported)
            return AdvSimd.Arm64.ZipLow(a, b);

        if (Sse2.IsSupported)
            return Sse2.UnpackLow(a, b);

        return Vector128.Create(a.ToScalar(), b.ToScalar());
    }

    /// <summary>(a1, b1).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<double> UnpackHigh(Vector128<double> a, Vector128<double> b)
    {
        if (AdvSimd.Arm64.IsSupported)
            return AdvSimd.Arm64.ZipHigh(a, b);

        if (Sse2.IsSupported)
            return Sse2.UnpackHigh(a, b);

        return Vector128.Create(a.GetElement(1), b.GetElement(1));
    }

    /// <summary>
    /// The transform with AVX2: four columns, then four rows, at once. The columns are stored transposed (two 4 x 4 blocks
    /// per group of columns). The quantized values are clamped in double precision, which makes their conversion to 32-bit
    /// integers exact (x64 has no 64-bit integer conversion before AVX-512).
    /// </summary>
    internal static void TransformAndQuantizeAvx2(ReadOnlySpan<float> samples, ReadOnlySpan<double> divisors, Span<double> scratch, Span<int> coefficients)
    {
        samples = samples[..64];
        divisors = divisors[..64];
        coefficients = coefficients[..64];
        ref var source = ref unsafe(MemoryMarshal.GetReference(samples));
        ref var columns = ref unsafe(MemoryMarshal.GetReference(scratch[..64]));
        for (var x = 0; x < 8; x += 4)
        {
            Transform(
                LoadSamples(ref source, x), LoadSamples(ref source, 8 + x), LoadSamples(ref source, 16 + x), LoadSamples(ref source, 24 + x),
                LoadSamples(ref source, 32 + x), LoadSamples(ref source, 40 + x), LoadSamples(ref source, 48 + x), LoadSamples(ref source, 56 + x),
                out var c0, out var c1, out var c2, out var c3, out var c4, out var c5, out var c6, out var c7);

            // Lane k of output v is columns[(x + k) * 8 + v]
            ref var destination = ref unsafe(Unsafe.Add(ref columns, x * 8));
            Transpose(ref c0, ref c1, ref c2, ref c3);
            Transpose(ref c4, ref c5, ref c6, ref c7);
            unsafe { c0.StoreUnsafe(ref destination, 0); }
            unsafe { c4.StoreUnsafe(ref destination, 4); }
            unsafe { c1.StoreUnsafe(ref destination, 8); }
            unsafe { c5.StoreUnsafe(ref destination, 12); }
            unsafe { c2.StoreUnsafe(ref destination, 16); }
            unsafe { c6.StoreUnsafe(ref destination, 20); }
            unsafe { c3.StoreUnsafe(ref destination, 24); }
            unsafe { c7.StoreUnsafe(ref destination, 28); }
        }

        ref var divisor = ref unsafe(MemoryMarshal.GetReference(divisors));
        ref var output = ref unsafe(MemoryMarshal.GetReference(coefficients));
        for (var v = 0; v < 8; v += 4)
        {
            Transform(
                Load(ref columns, v), Load(ref columns, 8 + v), Load(ref columns, 16 + v), Load(ref columns, 24 + v),
                Load(ref columns, 32 + v), Load(ref columns, 40 + v), Load(ref columns, 48 + v), Load(ref columns, 56 + v),
                out var f0, out var f1, out var f2, out var f3, out var f4, out var f5, out var f6, out var f7);
            QuantizeAvx2(f0, ref divisor, ref output, v);
            QuantizeAvx2(f1, ref divisor, ref output, 8 + v);
            QuantizeAvx2(f2, ref divisor, ref output, 16 + v);
            QuantizeAvx2(f3, ref divisor, ref output, 24 + v);
            QuantizeAvx2(f4, ref divisor, ref output, 32 + v);
            QuantizeAvx2(f5, ref divisor, ref output, 40 + v);
            QuantizeAvx2(f6, ref divisor, ref output, 48 + v);
            QuantizeAvx2(f7, ref divisor, ref output, 56 + v);
        }

        static Vector256<double> LoadSamples(ref float source, int index) => Avx.ConvertToVector256Double(unsafe(Vector128.LoadUnsafe(ref source, (nuint)index)));

        static Vector256<double> Load(ref double source, int index) => unsafe(Vector256.LoadUnsafe(ref source, (nuint)index));

        // Transposes the 4 x 4 block of the rows a, b, c, d
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void Transpose(ref Vector256<double> a, ref Vector256<double> b, ref Vector256<double> c, ref Vector256<double> d)
        {
            var ab0 = Avx.UnpackLow(a, b);
            var ab1 = Avx.UnpackHigh(a, b);
            var cd0 = Avx.UnpackLow(c, d);
            var cd1 = Avx.UnpackHigh(c, d);
            a = Avx.Permute2x128(ab0, cd0, 0x20);
            b = Avx.Permute2x128(ab1, cd1, 0x20);
            c = Avx.Permute2x128(ab0, cd0, 0x31);
            d = Avx.Permute2x128(ab1, cd1, 0x31);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Transform(Vector256<double> x0, Vector256<double> x1, Vector256<double> x2, Vector256<double> x3, Vector256<double> x4, Vector256<double> x5, Vector256<double> x6, Vector256<double> x7,
        out Vector256<double> y0, out Vector256<double> y1, out Vector256<double> y2, out Vector256<double> y3, out Vector256<double> y4, out Vector256<double> y5, out Vector256<double> y6, out Vector256<double> y7)
    {
        var s07 = x0 + x7;
        var s16 = x1 + x6;
        var s25 = x2 + x5;
        var s34 = x3 + x4;
        var d07 = x0 - x7;
        var d16 = x1 - x6;
        var d25 = x2 - x5;
        var d34 = x3 - x4;
        var a0 = s07 + s34;
        var a1 = s16 + s25;
        var a2 = s07 - s34;
        var a3 = s16 - s25;
        y0 = (a0 + a1) * C4;
        y4 = (a0 - a1) * C4;
        y2 = (a2 * C2) + (a3 * C6);
        y6 = (a2 * C6) - (a3 * C2);
        y1 = (d07 * C1) + (d16 * C3) + (d25 * C5) + (d34 * C7);
        y3 = (d07 * C3) - (d16 * C7) - (d25 * C1) - (d34 * C5);
        y5 = (d07 * C5) - (d16 * C1) + (d25 * C7) + (d34 * C3);
        y7 = (d07 * C7) - (d16 * C5) + (d25 * C3) - (d34 * C1);
    }

    /// <summary>Quantizes the coefficients of the transposed indexes <paramref name="index"/> to <paramref name="index"/> + 3, and writes them at their zig-zag positions.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void QuantizeAvx2(Vector256<double> coefficient, ref double divisors, ref int destination, int index)
    {
        var quotient = coefficient / unsafe(Vector256.LoadUnsafe(ref divisors, (nuint)index));
        var minimum = index == 0 ? Vector256.Create((double)MinDc, -MaxAcMagnitude, -MaxAcMagnitude, -MaxAcMagnitude) : Vector256.Create((double)-MaxAcMagnitude);
        var maximum = index == 0 ? Vector256.Create((double)MaxDc, MaxAcMagnitude, MaxAcMagnitude, MaxAcMagnitude) : Vector256.Create((double)MaxAcMagnitude);
        var clamped = Vector256.MinNative(Vector256.MaxNative(quotient, minimum), maximum);

        // trunc(q) plus or minus 1 when the exact fraction reaches one half
        var half = Vector256.Create(0.5);
        var truncated = Avx.RoundToZero(clamped);
        var fraction = clamped - truncated;
        var rounded = truncated
            + (Vector256.GreaterThanOrEqual(fraction, half) & Vector256<double>.One)
            - (Vector256.LessThanOrEqual(fraction, -half) & Vector256<double>.One);
        var integers = Avx.ConvertToVector128Int32WithTruncation(rounded);
        ref var positions = ref unsafe(Unsafe.Add(ref MemoryMarshal.GetReference(TransposedZigZagPositions), index));
        unsafe { Unsafe.Add(ref destination, positions) = integers.ToScalar(); }
        unsafe { Unsafe.Add(ref destination, Unsafe.Add(ref positions, 1)) = integers.GetElement(1); }
        unsafe { Unsafe.Add(ref destination, Unsafe.Add(ref positions, 2)) = integers.GetElement(2); }
        unsafe { Unsafe.Add(ref destination, Unsafe.Add(ref positions, 3)) = integers.GetElement(3); }
    }

    /// <summary>
    /// The transform with AVX-512: the eight columns at once, an 8 x 8 transposition in registers, then the eight rows
    /// at once. The quantized values of the transposed order are reordered to the zig-zag order by two-source
    /// permutations of 16 values each (<see cref="ZigZagTransposedIndexes"/>).
    /// </summary>
    internal static void TransformAndQuantizeAvx512(ReadOnlySpan<float> samples, ReadOnlySpan<double> divisors, Span<int> coefficients)
    {
        samples = samples[..64];
        divisors = divisors[..64];
        coefficients = coefficients[..64];
        ref var source = ref unsafe(MemoryMarshal.GetReference(samples));
        Transform(
            LoadRow(ref source, 0), LoadRow(ref source, 8), LoadRow(ref source, 16), LoadRow(ref source, 24),
            LoadRow(ref source, 32), LoadRow(ref source, 40), LoadRow(ref source, 48), LoadRow(ref source, 56),
            out var c0, out var c1, out var c2, out var c3, out var c4, out var c5, out var c6, out var c7);

        // Lane x of c_v is the output v of the column x; after the transposition, lane v of x_k is the output v of the column k
        var t0 = Avx512F.UnpackLow(c0, c1);
        var t1 = Avx512F.UnpackHigh(c0, c1);
        var t2 = Avx512F.UnpackLow(c2, c3);
        var t3 = Avx512F.UnpackHigh(c2, c3);
        var t4 = Avx512F.UnpackLow(c4, c5);
        var t5 = Avx512F.UnpackHigh(c4, c5);
        var t6 = Avx512F.UnpackLow(c6, c7);
        var t7 = Avx512F.UnpackHigh(c6, c7);
        var even = Vector512.Create(0L, 1, 8, 9, 4, 5, 12, 13);
        var odd = Vector512.Create(2L, 3, 10, 11, 6, 7, 14, 15);
        var u0 = Avx512F.PermuteVar8x64x2(t0, even, t2);
        var u1 = Avx512F.PermuteVar8x64x2(t0, odd, t2);
        var u2 = Avx512F.PermuteVar8x64x2(t1, even, t3);
        var u3 = Avx512F.PermuteVar8x64x2(t1, odd, t3);
        var u4 = Avx512F.PermuteVar8x64x2(t4, even, t6);
        var u5 = Avx512F.PermuteVar8x64x2(t4, odd, t6);
        var u6 = Avx512F.PermuteVar8x64x2(t5, even, t7);
        var u7 = Avx512F.PermuteVar8x64x2(t5, odd, t7);
        Transform(
            Avx512F.Shuffle4x128(u0, u4, 0x44), Avx512F.Shuffle4x128(u2, u6, 0x44), Avx512F.Shuffle4x128(u1, u5, 0x44), Avx512F.Shuffle4x128(u3, u7, 0x44),
            Avx512F.Shuffle4x128(u0, u4, 0xEE), Avx512F.Shuffle4x128(u2, u6, 0xEE), Avx512F.Shuffle4x128(u1, u5, 0xEE), Avx512F.Shuffle4x128(u3, u7, 0xEE),
            out var f0, out var f1, out var f2, out var f3, out var f4, out var f5, out var f6, out var f7);

        // Lane v of f_u is the transposed index u * 8 + v
        ref var divisor = ref unsafe(MemoryMarshal.GetReference(divisors));
        var first = Vector512.Create(QuantizeAvx512(f0, ref divisor, 0), QuantizeAvx512(f1, ref divisor, 8));
        var second = Vector512.Create(QuantizeAvx512(f2, ref divisor, 16), QuantizeAvx512(f3, ref divisor, 24));
        var third = Vector512.Create(QuantizeAvx512(f4, ref divisor, 32), QuantizeAvx512(f5, ref divisor, 40));
        var fourth = Vector512.Create(QuantizeAvx512(f6, ref divisor, 48), QuantizeAvx512(f7, ref divisor, 56));
        ref var indexes = ref unsafe(MemoryMarshal.GetReference(ZigZagTransposedIndexes));
        ref var output = ref unsafe(MemoryMarshal.GetReference(coefficients));
        for (var i = 0; i < 64; i += 16)
        {
            // The permutations read bits 0 to 4 of each index: the values of the first or the second half
            var index = unsafe(Vector512.LoadUnsafe(ref indexes, (nuint)i));
            var low = Avx512F.PermuteVar16x32x2(first, index, second);
            var high = Avx512F.PermuteVar16x32x2(third, index, fourth);
            unsafe { Vector512.ConditionalSelect(Vector512.GreaterThan(index, Vector512.Create(31)), high, low).StoreUnsafe(ref output, (nuint)i); }
        }

        static Vector512<double> LoadRow(ref float source, int index) => Avx512F.ConvertToVector512Double(unsafe(Vector256.LoadUnsafe(ref source, (nuint)index)));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Transform(Vector512<double> x0, Vector512<double> x1, Vector512<double> x2, Vector512<double> x3, Vector512<double> x4, Vector512<double> x5, Vector512<double> x6, Vector512<double> x7,
        out Vector512<double> y0, out Vector512<double> y1, out Vector512<double> y2, out Vector512<double> y3, out Vector512<double> y4, out Vector512<double> y5, out Vector512<double> y6, out Vector512<double> y7)
    {
        var s07 = x0 + x7;
        var s16 = x1 + x6;
        var s25 = x2 + x5;
        var s34 = x3 + x4;
        var d07 = x0 - x7;
        var d16 = x1 - x6;
        var d25 = x2 - x5;
        var d34 = x3 - x4;
        var a0 = s07 + s34;
        var a1 = s16 + s25;
        var a2 = s07 - s34;
        var a3 = s16 - s25;
        y0 = (a0 + a1) * C4;
        y4 = (a0 - a1) * C4;
        y2 = (a2 * C2) + (a3 * C6);
        y6 = (a2 * C6) - (a3 * C2);
        y1 = (d07 * C1) + (d16 * C3) + (d25 * C5) + (d34 * C7);
        y3 = (d07 * C3) - (d16 * C7) - (d25 * C1) - (d34 * C5);
        y5 = (d07 * C5) - (d16 * C1) + (d25 * C7) + (d34 * C3);
        y7 = (d07 * C7) - (d16 * C5) + (d25 * C3) - (d34 * C1);
    }

    /// <summary>Quantizes the coefficients of the transposed indexes <paramref name="index"/> to <paramref name="index"/> + 7, like <see cref="QuantizeAvx2"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<int> QuantizeAvx512(Vector512<double> coefficient, ref double divisors, int index)
    {
        var quotient = coefficient / unsafe(Vector512.LoadUnsafe(ref divisors, (nuint)index));
        var minimum = index == 0 ? Vector512.Create((double)MinDc, -MaxAcMagnitude, -MaxAcMagnitude, -MaxAcMagnitude, -MaxAcMagnitude, -MaxAcMagnitude, -MaxAcMagnitude, -MaxAcMagnitude) : Vector512.Create((double)-MaxAcMagnitude);
        var maximum = index == 0 ? Vector512.Create((double)MaxDc, MaxAcMagnitude, MaxAcMagnitude, MaxAcMagnitude, MaxAcMagnitude, MaxAcMagnitude, MaxAcMagnitude, MaxAcMagnitude) : Vector512.Create((double)MaxAcMagnitude);
        var clamped = Vector512.MinNative(Vector512.MaxNative(quotient, minimum), maximum);

        // trunc(q) plus or minus 1 when the exact fraction reaches one half (VRNDSCALEPD with the truncation mode, no exception)
        var half = Vector512.Create(0.5);
        var truncated = Avx512F.RoundScale(clamped, 0x0B);
        var fraction = clamped - truncated;
        var rounded = truncated
            + (Vector512.GreaterThanOrEqual(fraction, half) & Vector512<double>.One)
            - (Vector512.LessThanOrEqual(fraction, -half) & Vector512<double>.One);
        return Avx512F.ConvertToVector256Int32WithTruncation(rounded);
    }
}
