using System.Numerics;
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
/// <c>C(0) = 1 / sqrt(2)</c>, evaluated separably in IEEE double precision (rows first, then columns, each pass a direct
/// 8-term sum in increasing index order) from the level-shifted samples, then <c>Sq(u, v) = round(F(u, v) / Q(u, v))</c>
/// to nearest with ties away from zero. The basis values <c>C(u)/2 cos((2x + 1) u pi / 16)</c> are taken from the same
/// literal constants as the inverse transform (<see cref="JpegIdct"/>), so results do not depend on the platform's
/// <c>Math.Cos</c>.
/// </summary>
/// <remarks>
/// With hardware vector support, both passes compute <see cref="Vector{T}.Count"/> outputs at once (two with
/// <see cref="Vector128{T}"/>, which keeps the four sums of a row or a column in flight together, and four with AVX2, which
/// keeps the eight sums of four rows or four columns in flight together): each lane performs the
/// same 8-term sum in the same order as the scalar reference (<see cref="TransformAndQuantizeScalar"/>), without fused
/// multiply-adds, and the quantization divides and rounds exactly like <see cref="Math.Round(double, MidpointRounding)"/>
/// (the fraction <c>x - trunc(x)</c> is exact, or the rounding instruction of Arm64 that rounds ties away from zero), so the
/// coefficients are identical.
/// </remarks>
internal static class JpegForwardDct
{
    // C(0)/2 = 1 / (2 * sqrt(2)) and cos(k * pi / 16) / 2 for k = 1..7 (identical to JpegIdct)
    private const double C0 = 0.35355339059327373;
    private const double C1 = 0.4903926402016152;
    private const double C2 = 0.46193976625564337;
    private const double C3 = 0.4157348061512726;
    private const double C4 = 0.35355339059327373;
    private const double C5 = 0.27778511650980114;
    private const double C6 = 0.19134171618254492;
    private const double C7 = 0.09754516100806417;

    /// <summary>The basis <c>B[u * 8 + x] = C(u)/2 cos((2x + 1) u pi / 16)</c>.</summary>
    private static readonly double[] Basis = CreateBasis();

    /// <summary>The transposed basis <c>T[x * 8 + u] = B[u * 8 + x]</c> (contiguous over <c>u</c>, for the vectorized row pass).</summary>
    private static readonly double[] TransposedBasis = Transpose(Basis);

    /// <summary>Gets the largest magnitude of a quantized AC coefficient in a baseline scan (size category 10).</summary>
    public const int MaxAcMagnitude = 1023;

    /// <summary>Gets the smallest quantized DC coefficient (an all-black block at quantization value 1 is -1024).</summary>
    public const int MinDc = -1024;

    /// <summary>Gets the largest quantized DC coefficient, so that every DC difference stays within category 11 (at most 2047).</summary>
    public const int MaxDc = 1023;

    /// <summary>Transforms and quantizes one block.</summary>
    /// <param name="samples">64 level-shifted samples (natural order: <c>samples[y * 8 + x]</c>, nominally -128 to 127.5).</param>
    /// <param name="quantization">64 quantization values (natural order).</param>
    /// <param name="scratch">64 doubles of scratch space.</param>
    /// <param name="coefficients">The 64 quantized coefficients, in zig-zag order.</param>
    public static void TransformAndQuantize(ReadOnlySpan<float> samples, ReadOnlySpan<ushort> quantization, Span<double> scratch, Span<int> coefficients)
    {
        if (Avx2.IsSupported)
        {
            TransformAndQuantizeAvx2(samples, quantization, scratch, coefficients);
        }
        else if (Vector128.IsHardwareAccelerated && Vector<double>.Count == 2)
        {
            TransformAndQuantizeVector128(samples, quantization, scratch, coefficients);
        }
        else if (Vector.IsHardwareAccelerated && Vector<double>.Count is 2 or 4 or 8)
        {
            TransformAndQuantizeVector(samples, quantization, scratch, coefficients);
        }
        else
        {
            TransformAndQuantizeScalar(samples, quantization, scratch, coefficients);
        }
    }

    /// <summary>The scalar reference of <see cref="TransformAndQuantize"/>.</summary>
    internal static void TransformAndQuantizeScalar(ReadOnlySpan<float> samples, ReadOnlySpan<ushort> quantization, Span<double> scratch, Span<int> coefficients)
    {
        var basis = Basis;
        var rows = scratch[..64];

        // Rows: rows[y * 8 + u] = sum_x s(x, y) B[u][x]
        for (var y = 0; y < 8; y++)
        {
            var line = samples.Slice(y * 8, 8);
            for (var u = 0; u < 8; u++)
            {
                var b = basis.AsSpan(u * 8, 8);
                var sum = 0d;
                for (var x = 0; x < 8; x++)
                {
                    sum += line[x] * b[x];
                }

                rows[(y * 8) + u] = sum;
            }
        }

        // Columns: F(u, v) = sum_y rows[y][u] B[v][y], then quantization (natural index v * 8 + u)
        var zigZag = JpegIdct.ZigZag;
        Span<int> natural = stackalloc int[64];
        for (var v = 0; v < 8; v++)
        {
            var b = basis.AsSpan(v * 8, 8);
            for (var u = 0; u < 8; u++)
            {
                var sum = 0d;
                for (var y = 0; y < 8; y++)
                {
                    sum += rows[(y * 8) + u] * b[y];
                }

                var index = (v * 8) + u;
                var quantized = (int)Math.Round(sum / quantization[index], MidpointRounding.AwayFromZero);
                natural[index] = index == 0 ? Math.Clamp(quantized, MinDc, MaxDc) : Math.Clamp(quantized, -MaxAcMagnitude, MaxAcMagnitude);
            }
        }

        for (var k = 0; k < 64; k++)
        {
            coefficients[k] = natural[zigZag[k]];
        }
    }

    /// <summary>
    /// The transform with <see cref="Vector128{T}"/>: the four sums of a row (then of a column) are accumulated together, so
    /// that their additions overlap, and the quantized coefficients are written at their zig-zag positions directly.
    /// </summary>
    internal static void TransformAndQuantizeVector128(ReadOnlySpan<float> samples, ReadOnlySpan<ushort> quantization, Span<double> scratch, Span<int> coefficients)
    {
        samples = samples[..64];
        quantization = quantization[..64];
        coefficients = coefficients[..64];
        ref var line = ref unsafe(MemoryMarshal.GetReference(samples));
        ref var rows = ref unsafe(MemoryMarshal.GetReference(scratch[..64]));
        ref var transposed = ref unsafe(MemoryMarshal.GetArrayDataReference(TransposedBasis));
        ref var basis = ref unsafe(MemoryMarshal.GetArrayDataReference(Basis));

        // Rows: rows[y * 8 + u] = sum_x s(x, y) B[u][x], for u = 0-1, 2-3, 4-5 and 6-7
        for (var y = 0; y < 8; y++)
        {
            var sum0 = Vector128<double>.Zero;
            var sum1 = Vector128<double>.Zero;
            var sum2 = Vector128<double>.Zero;
            var sum3 = Vector128<double>.Zero;
            for (var x = 0; x < 8; x++)
            {
                var sample = Vector128.Create((double)unsafe(Unsafe.Add(ref line, (y * 8) + x)));
                ref var terms = ref unsafe(Unsafe.Add(ref transposed, x * 8));
                sum0 += sample * Load(ref terms, 0);
                sum1 += sample * Load(ref terms, 2);
                sum2 += sample * Load(ref terms, 4);
                sum3 += sample * Load(ref terms, 6);
            }

            unsafe { sum0.StoreUnsafe(ref rows, (nuint)(y * 8)); }
            unsafe { sum1.StoreUnsafe(ref rows, (nuint)((y * 8) + 2)); }
            unsafe { sum2.StoreUnsafe(ref rows, (nuint)((y * 8) + 4)); }
            unsafe { sum3.StoreUnsafe(ref rows, (nuint)((y * 8) + 6)); }
        }

        // Columns: F(u, v) = sum_y rows[y][u] B[v][y], then quantization (natural index v * 8 + u)
        for (var v = 0; v < 8; v++)
        {
            var sum0 = Vector128<double>.Zero;
            var sum1 = Vector128<double>.Zero;
            var sum2 = Vector128<double>.Zero;
            var sum3 = Vector128<double>.Zero;
            for (var y = 0; y < 8; y++)
            {
                var term = Vector128.Create(unsafe(Unsafe.Add(ref basis, (v * 8) + y)));
                ref var row = ref unsafe(Unsafe.Add(ref rows, y * 8));
                sum0 += Load(ref row, 0) * term;
                sum1 += Load(ref row, 2) * term;
                sum2 += Load(ref row, 4) * term;
                sum3 += Load(ref row, 6) * term;
            }

            Quantize(sum0, quantization, (v * 8) + 0, coefficients);
            Quantize(sum1, quantization, (v * 8) + 2, coefficients);
            Quantize(sum2, quantization, (v * 8) + 4, coefficients);
            Quantize(sum3, quantization, (v * 8) + 6, coefficients);
        }

        static Vector128<double> Load(ref double source, int index) => unsafe(Vector128.LoadUnsafe(ref source, (nuint)index));
    }

    /// <summary>Quantizes the coefficients of the natural indexes <paramref name="index"/> and <paramref name="index"/> + 1, and writes them at their zig-zag positions.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Quantize(Vector128<double> sum, ReadOnlySpan<ushort> quantization, int index, Span<int> coefficients)
    {
        var quotient = sum / Vector128.Create((double)quantization[index], quantization[index + 1]);
        Vector128<double> rounded;
        if (AdvSimd.Arm64.IsSupported)
        {
            // FRINTA: to nearest, ties away from zero
            rounded = AdvSimd.Arm64.RoundAwayFromZero(quotient);
        }
        else
        {
            // trunc(q) plus or minus 1 when the exact fraction reaches one half
            var half = Vector128.Create(0.5);
            var truncated = Vector128.Truncate(quotient);
            var fraction = quotient - truncated;
            rounded = truncated
                + Vector128.ConditionalSelect(Vector128.GreaterThanOrEqual(fraction, half), Vector128<double>.One, Vector128<double>.Zero)
                - Vector128.ConditionalSelect(Vector128.LessThanOrEqual(fraction, -half), Vector128<double>.One, Vector128<double>.Zero);
        }

        // The clamping of the scalar reference, on the integral values: the DC coefficient (index 0) has its own range
        var minimum = index == 0 ? Vector128.Create((double)MinDc, -MaxAcMagnitude) : Vector128.Create((double)-MaxAcMagnitude);
        var maximum = index == 0 ? Vector128.Create((double)MaxDc, MaxAcMagnitude) : Vector128.Create((double)MaxAcMagnitude);
        var clamped = Vector128.Min(Vector128.Max(rounded, minimum), maximum);
        var positions = ZigZagPositions;
        if (Sse2.IsSupported)
        {
            // x64 has no 64-bit integer conversion before AVX-512: CVTTPD2DQ, exact on the clamped values
            var converted = Sse2.ConvertToVector128Int32WithTruncation(clamped);
            coefficients[positions[index]] = converted.ToScalar();
            coefficients[positions[index + 1]] = converted.GetElement(1);
            return;
        }

        var integers = Vector128.ConvertToInt64(clamped);
        coefficients[positions[index]] = (int)integers.ToScalar();
        coefficients[positions[index + 1]] = (int)integers.GetElement(1);
    }

    /// <summary>
    /// The transform with AVX2: four rows (then four columns) at once, so that eight sums of four values are in flight
    /// together and each loaded vector of the basis (then of the row pass) is shared by four of them. The quantized values
    /// are clamped in double precision, which makes their conversion to 32-bit integers exact (x64 has no 64-bit integer
    /// conversion before AVX-512).
    /// </summary>
    internal static void TransformAndQuantizeAvx2(ReadOnlySpan<float> samples, ReadOnlySpan<ushort> quantization, Span<double> scratch, Span<int> coefficients)
    {
        samples = samples[..64];
        quantization = quantization[..64];
        coefficients = coefficients[..64];
        ref var line = ref unsafe(MemoryMarshal.GetReference(samples));
        ref var rows = ref unsafe(MemoryMarshal.GetReference(scratch[..64]));
        ref var transposed = ref unsafe(MemoryMarshal.GetArrayDataReference(TransposedBasis));
        ref var basis = ref unsafe(MemoryMarshal.GetArrayDataReference(Basis));

        // Rows: rows[y * 8 + u] = sum_x s(x, y) B[u][x], for u = 0-3 and 4-7 of the rows y to y + 3
        for (var y = 0; y < 8; y += 4)
        {
            ref var first = ref unsafe(Unsafe.Add(ref line, y * 8));
            var low0 = Vector256<double>.Zero;
            var high0 = Vector256<double>.Zero;
            var low1 = Vector256<double>.Zero;
            var high1 = Vector256<double>.Zero;
            var low2 = Vector256<double>.Zero;
            var high2 = Vector256<double>.Zero;
            var low3 = Vector256<double>.Zero;
            var high3 = Vector256<double>.Zero;
            for (var x = 0; x < 8; x++)
            {
                ref var terms = ref unsafe(Unsafe.Add(ref transposed, x * 8));
                var low = Load(ref terms, 0);
                var high = Load(ref terms, 4);
                var sample0 = Vector256.Create((double)unsafe(Unsafe.Add(ref first, x)));
                var sample1 = Vector256.Create((double)unsafe(Unsafe.Add(ref first, 8 + x)));
                var sample2 = Vector256.Create((double)unsafe(Unsafe.Add(ref first, 16 + x)));
                var sample3 = Vector256.Create((double)unsafe(Unsafe.Add(ref first, 24 + x)));
                low0 += sample0 * low;
                high0 += sample0 * high;
                low1 += sample1 * low;
                high1 += sample1 * high;
                low2 += sample2 * low;
                high2 += sample2 * high;
                low3 += sample3 * low;
                high3 += sample3 * high;
            }

            ref var output = ref unsafe(Unsafe.Add(ref rows, y * 8));
            unsafe { low0.StoreUnsafe(ref output, 0); }
            unsafe { high0.StoreUnsafe(ref output, 4); }
            unsafe { low1.StoreUnsafe(ref output, 8); }
            unsafe { high1.StoreUnsafe(ref output, 12); }
            unsafe { low2.StoreUnsafe(ref output, 16); }
            unsafe { high2.StoreUnsafe(ref output, 20); }
            unsafe { low3.StoreUnsafe(ref output, 24); }
            unsafe { high3.StoreUnsafe(ref output, 28); }
        }

        // Columns: F(u, v) = sum_y rows[y][u] B[v][y], for u = 0-3 and 4-7 of the columns v to v + 3, then quantization
        for (var v = 0; v < 8; v += 4)
        {
            ref var terms = ref unsafe(Unsafe.Add(ref basis, v * 8));
            var low0 = Vector256<double>.Zero;
            var high0 = Vector256<double>.Zero;
            var low1 = Vector256<double>.Zero;
            var high1 = Vector256<double>.Zero;
            var low2 = Vector256<double>.Zero;
            var high2 = Vector256<double>.Zero;
            var low3 = Vector256<double>.Zero;
            var high3 = Vector256<double>.Zero;
            for (var y = 0; y < 8; y++)
            {
                ref var row = ref unsafe(Unsafe.Add(ref rows, y * 8));
                var low = Load(ref row, 0);
                var high = Load(ref row, 4);
                var term0 = Vector256.Create(unsafe(Unsafe.Add(ref terms, y)));
                var term1 = Vector256.Create(unsafe(Unsafe.Add(ref terms, 8 + y)));
                var term2 = Vector256.Create(unsafe(Unsafe.Add(ref terms, 16 + y)));
                var term3 = Vector256.Create(unsafe(Unsafe.Add(ref terms, 24 + y)));
                low0 += low * term0;
                high0 += high * term0;
                low1 += low * term1;
                high1 += high * term1;
                low2 += low * term2;
                high2 += high * term2;
                low3 += low * term3;
                high3 += high * term3;
            }

            QuantizeAvx2(low0, high0, quantization, v * 8, coefficients);
            QuantizeAvx2(low1, high1, quantization, (v + 1) * 8, coefficients);
            QuantizeAvx2(low2, high2, quantization, (v + 2) * 8, coefficients);
            QuantizeAvx2(low3, high3, quantization, (v + 3) * 8, coefficients);
        }

        static Vector256<double> Load(ref double source, int index) => unsafe(Vector256.LoadUnsafe(ref source, (nuint)index));
    }

    /// <summary>Quantizes the eight coefficients of the natural indexes <paramref name="index"/> to <paramref name="index"/> + 7, and writes them at their zig-zag positions.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void QuantizeAvx2(Vector256<double> low, Vector256<double> high, ReadOnlySpan<ushort> quantization, int index, Span<int> coefficients)
    {
        // The quantization values, widened to 32-bit integers and converted exactly
        var divisors = Avx2.ConvertToVector256Int32(unsafe(Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(quantization), (nuint)index)));
        var first = Round(low / Avx.ConvertToVector256Double(divisors.GetLower()), index == 0);
        var second = Round(high / Avx.ConvertToVector256Double(divisors.GetUpper()), isDc: false);
        ref var destination = ref unsafe(MemoryMarshal.GetReference(coefficients));
        ref var positions = ref unsafe(Unsafe.Add(ref MemoryMarshal.GetReference(ZigZagPositions), index));
        unsafe { Unsafe.Add(ref destination, positions) = first.ToScalar(); }
        unsafe { Unsafe.Add(ref destination, Unsafe.Add(ref positions, 1)) = first.GetElement(1); }
        unsafe { Unsafe.Add(ref destination, Unsafe.Add(ref positions, 2)) = first.GetElement(2); }
        unsafe { Unsafe.Add(ref destination, Unsafe.Add(ref positions, 3)) = first.GetElement(3); }
        unsafe { Unsafe.Add(ref destination, Unsafe.Add(ref positions, 4)) = second.ToScalar(); }
        unsafe { Unsafe.Add(ref destination, Unsafe.Add(ref positions, 5)) = second.GetElement(1); }
        unsafe { Unsafe.Add(ref destination, Unsafe.Add(ref positions, 6)) = second.GetElement(2); }
        unsafe { Unsafe.Add(ref destination, Unsafe.Add(ref positions, 7)) = second.GetElement(3); }

        // To nearest, ties away from zero: trunc(q) plus or minus 1 when the exact fraction reaches one half; then the
        // clamping of the scalar reference (the DC coefficient, lane 0 of the first vector, has its own range), after which
        // the conversion is exact
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Vector128<int> Round(Vector256<double> quotient, bool isDc)
        {
            var half = Vector256.Create(0.5);
            var one = Vector256<double>.One;
            var truncated = Avx.RoundToZero(quotient);
            var fraction = quotient - truncated;
            var rounded = truncated + (Avx.CompareGreaterThanOrEqual(fraction, half) & one) - (Avx.CompareLessThanOrEqual(fraction, -half) & one);
            var minimum = isDc ? Vector256.Create((double)MinDc, -MaxAcMagnitude, -MaxAcMagnitude, -MaxAcMagnitude) : Vector256.Create((double)-MaxAcMagnitude);
            var maximum = isDc ? Vector256.Create((double)MaxDc, MaxAcMagnitude, MaxAcMagnitude, MaxAcMagnitude) : Vector256.Create((double)MaxAcMagnitude);
            return Avx.ConvertToVector128Int32WithTruncation(Avx.Min(Avx.Max(rounded, minimum), maximum));
        }
    }

    /// <summary>Gets the zig-zag position of each natural index (the inverse of <see cref="JpegIdct.ZigZag"/>).</summary>
    private static ReadOnlySpan<byte> ZigZagPositions =>
    [
        0, 1, 5, 6, 14, 15, 27, 28,
        2, 4, 7, 13, 16, 26, 29, 42,
        3, 8, 12, 17, 25, 30, 41, 43,
        9, 11, 18, 24, 31, 40, 44, 53,
        10, 19, 23, 32, 39, 45, 52, 54,
        20, 22, 33, 38, 46, 51, 55, 60,
        21, 34, 37, 47, 50, 56, 59, 61,
        35, 36, 48, 49, 57, 58, 62, 63,
    ];

    /// <summary>The transform with <see cref="Vector{T}"/>, <see cref="Vector{T}.Count"/> outputs at once.</summary>
    internal static void TransformAndQuantizeVector(ReadOnlySpan<float> samples, ReadOnlySpan<ushort> quantization, Span<double> scratch, Span<int> coefficients)
    {
        samples = samples[..64];
        quantization = quantization[..64];
        coefficients = coefficients[..64];
        ref var rows = ref unsafe(MemoryMarshal.GetReference(scratch[..64]));
        ref var transposed = ref unsafe(MemoryMarshal.GetArrayDataReference(TransposedBasis));
        var basis = Basis;
        var count = Vector<double>.Count;

        // Rows: rows[y * 8 + u] = sum_x s(x, y) B[u][x], count values of u at once
        for (var y = 0; y < 8; y++)
        {
            var line = samples.Slice(y * 8, 8);
            for (var u = 0; u < 8; u += count)
            {
                var sum = Vector<double>.Zero;
                for (var x = 0; x < 8; x++)
                {
                    sum += new Vector<double>(line[x]) * unsafe(Vector.LoadUnsafe(ref transposed, (nuint)((x * 8) + u)));
                }

                unsafe { sum.StoreUnsafe(ref rows, (nuint)((y * 8) + u)); }
            }
        }

        // Columns: F(u, v) = sum_y rows[y][u] B[v][y], then quantization, count values of u at once
        Span<int> natural = stackalloc int[64];
        Span<double> divisors = stackalloc double[8];
        var half = new Vector<double>(0.5);
        for (var v = 0; v < 8; v++)
        {
            var b = basis.AsSpan(v * 8, 8);
            for (var i = 0; i < 8; i++)
            {
                divisors[i] = quantization[(v * 8) + i];
            }

            for (var u = 0; u < 8; u += count)
            {
                var sum = Vector<double>.Zero;
                for (var y = 0; y < 8; y++)
                {
                    sum += unsafe(Vector.LoadUnsafe(ref rows, (nuint)((y * 8) + u))) * new Vector<double>(b[y]);
                }

                // Round to nearest, ties away from zero: trunc(q) plus or minus 1 when the exact fraction reaches one half
                var quotient = sum / new Vector<double>(divisors[u..]);
                var truncated = Vector.Truncate(quotient);
                var fraction = quotient - truncated;
                var rounded = truncated
                    + Vector.ConditionalSelect(Vector.GreaterThanOrEqual(fraction, half), Vector<double>.One, Vector<double>.Zero)
                    - Vector.ConditionalSelect(Vector.LessThanOrEqual(fraction, -half), Vector<double>.One, Vector<double>.Zero);
                var integers = Vector.ConvertToInt64(rounded);
                for (var k = 0; k < count; k++)
                {
                    var index = (v * 8) + u + k;
                    var quantized = integers.GetElement(k);
                    natural[index] = (int)(index == 0 ? Math.Clamp(quantized, MinDc, MaxDc) : Math.Clamp(quantized, -MaxAcMagnitude, MaxAcMagnitude));
                }
            }
        }

        var zigZag = JpegIdct.ZigZag;
        for (var k = 0; k < 64; k++)
        {
            coefficients[k] = natural[zigZag[k]];
        }
    }

    private static double[] Transpose(double[] values)
    {
        var result = new double[64];
        for (var i = 0; i < 8; i++)
        {
            for (var j = 0; j < 8; j++)
            {
                result[(j * 8) + i] = values[(i * 8) + j];
            }
        }

        return result;
    }

    private static double[] CreateBasis()
    {
        // cos(m * pi / 16) / 2 for m = 0..8; the factor C(0) = 1/sqrt(2) is applied to row u = 0
        ReadOnlySpan<double> halfCosines = [0.5, C1, C2, C3, C4, C5, C6, C7, 0];
        var basis = new double[64];
        for (var u = 0; u < 8; u++)
        {
            for (var x = 0; x < 8; x++)
            {
                if (u == 0)
                {
                    basis[x] = C0;
                    continue;
                }

                // cos(m pi / 16) for m = (2x + 1) u, reduced with the symmetries of the cosine
                var m = ((2 * x) + 1) * u % 32;
                if (m > 16)
                {
                    m = 32 - m; // cos(2 pi - t) = cos(t)
                }

                basis[(u * 8) + x] = m <= 8 ? halfCosines[m] : -halfCosines[16 - m]; // cos(pi - t) = -cos(t)
            }
        }

        return basis;
    }
}
