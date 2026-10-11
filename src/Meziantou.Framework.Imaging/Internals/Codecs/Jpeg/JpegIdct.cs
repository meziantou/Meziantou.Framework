using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The 8x8 inverse DCT of the JPEG decoders (ITU-T T.81 section A.3.3), with this numerical contract:
/// a separable transform in IEEE double precision (columns first, then rows; each 8-point pass uses the
/// even/odd decomposition with the constants <c>C(k)/2 * cos(k*pi/16)</c> written below as literals, so results do not depend
/// on the platform's <c>Math.Cos</c>), the level shift +128, rounding to nearest with ties upward (<c>floor(x + 0.5)</c>),
/// and clamping to 0..255. A block whose AC coefficients are all zero is computed exactly in integers:
/// <c>floor((DC * Q + 4) / 8) + 128</c>, clamped.
/// </summary>
/// <remarks>
/// With hardware vector support, <see cref="Transform"/> runs each pass on <see cref="Vector{T}.Count"/> columns (then rows)
/// at once: every lane performs exactly the operations of the scalar reference (<see cref="TransformScalar"/>) in the same
/// order, without fused multiply-adds, so the samples are bit-identical. The scalar shortcut for columns without
/// AC coefficients is skipped there; it is itself exact (the full formula reduces to <c>x0 * C0</c> when the other inputs are
/// zero).
/// </remarks>
internal static class JpegIdct
{
    // C(0)/2 = 1 / (2 * sqrt(2)) and cos(k * pi / 16) / 2 for k = 1..7
    private const double C0 = 0.35355339059327373;
    private const double C1 = 0.4903926402016152;
    private const double C2 = 0.46193976625564337;
    private const double C3 = 0.4157348061512726;
    private const double C5 = 0.27778511650980114;
    private const double C6 = 0.19134171618254492;
    private const double C7 = 0.09754516100806417;

    /// <summary>Gets the natural (row-major) index of each coefficient in zig-zag order (T.81 figure A.6).</summary>
    public static ReadOnlySpan<byte> ZigZag =>
    [
        0, 1, 8, 16, 9, 2, 3, 10,
        17, 24, 32, 25, 18, 11, 4, 5,
        12, 19, 26, 33, 40, 48, 41, 34,
        27, 20, 13, 6, 7, 14, 21, 28,
        35, 42, 49, 56, 57, 50, 43, 36,
        29, 22, 15, 23, 30, 37, 44, 51,
        58, 59, 52, 45, 38, 31, 39, 46,
        53, 60, 61, 54, 47, 55, 62, 63,
    ];

    /// <summary>Writes the samples of a block whose AC coefficients are all zero.</summary>
    /// <param name="dequantizedDc">The DC coefficient multiplied by its quantization value.</param>
    /// <param name="destination">The first sample of the block.</param>
    /// <param name="stride">The distance between two sample rows.</param>
    public static void TransformDcOnly(long dequantizedDc, Span<byte> destination, int stride)
    {
        var value = (byte)Math.Clamp(((dequantizedDc + 4) >> 3) + 128, 0, 255);
        for (var y = 0; y < 8; y++)
        {
            destination.Slice(y * stride, 8).Fill(value);
        }
    }

    /// <summary>Transforms dequantized coefficients (natural order) into 8x8 samples.</summary>
    /// <param name="coefficients">64 dequantized coefficients, natural order; overwritten (used as the intermediate buffer).</param>
    /// <param name="destination">The first sample of the block.</param>
    /// <param name="stride">The distance between two sample rows.</param>
    public static void Transform(Span<double> coefficients, Span<byte> destination, int stride)
    {
        if (Vector.IsHardwareAccelerated && Vector<double>.Count is 2 or 4 or 8)
        {
            TransformVector(coefficients, destination, stride);
        }
        else
        {
            TransformScalar(coefficients, destination, stride);
        }
    }

    /// <summary>The scalar reference of <see cref="Transform"/>.</summary>
    internal static void TransformScalar(Span<double> coefficients, Span<byte> destination, int stride)
    {
        var block = coefficients[..64];

        // Columns: block[v * 8 + u] -> block[y * 8 + u]
        for (var u = 0; u < 8; u++)
        {
            var x0 = block[u];
            var x1 = block[8 + u];
            var x2 = block[16 + u];
            var x3 = block[24 + u];
            var x4 = block[32 + u];
            var x5 = block[40 + u];
            var x6 = block[48 + u];
            var x7 = block[56 + u];
            if (x1 == 0 && x2 == 0 && x3 == 0 && x4 == 0 && x5 == 0 && x6 == 0 && x7 == 0)
            {
                var dc = x0 * C0;
                for (var y = 0; y < 64; y += 8)
                {
                    block[y + u] = dc;
                }

                continue;
            }

            Transform1D(x0, x1, x2, x3, x4, x5, x6, x7, out var o0, out var o1, out var o2, out var o3, out var o4, out var o5, out var o6, out var o7);
            block[u] = o0;
            block[8 + u] = o1;
            block[16 + u] = o2;
            block[24 + u] = o3;
            block[32 + u] = o4;
            block[40 + u] = o5;
            block[48 + u] = o6;
            block[56 + u] = o7;
        }

        // Rows: block[y * 8 + u] -> samples
        for (var y = 0; y < 8; y++)
        {
            var row = block.Slice(y * 8, 8);
            Transform1D(row[0], row[1], row[2], row[3], row[4], row[5], row[6], row[7], out var o0, out var o1, out var o2, out var o3, out var o4, out var o5, out var o6, out var o7);
            var output = destination.Slice(y * stride, 8);
            output[0] = ToSample(o0);
            output[1] = ToSample(o1);
            output[2] = ToSample(o2);
            output[3] = ToSample(o3);
            output[4] = ToSample(o4);
            output[5] = ToSample(o5);
            output[6] = ToSample(o6);
            output[7] = ToSample(o7);
        }
    }

    private static void TransformVector(Span<double> coefficients, Span<byte> destination, int stride)
    {
        // Bounds are checked once here; the loops below use unchecked references
        ArgumentOutOfRangeException.ThrowIfLessThan(coefficients.Length, 64);
        ArgumentOutOfRangeException.ThrowIfLessThan(destination.Length, (7 * stride) + 8);
        ref var block = ref unsafe(MemoryMarshal.GetReference(coefficients));
        ref var output = ref unsafe(MemoryMarshal.GetReference(destination));
        var count = Vector<double>.Count;

        // Columns, count at a time: block[v * 8 + u] -> block[y * 8 + u]
        for (var u = 0; u < 8; u += count)
        {
            Transform1D(
                Load(ref block, u), Load(ref block, 8 + u), Load(ref block, 16 + u), Load(ref block, 24 + u),
                Load(ref block, 32 + u), Load(ref block, 40 + u), Load(ref block, 48 + u), Load(ref block, 56 + u),
                out var o0, out var o1, out var o2, out var o3, out var o4, out var o5, out var o6, out var o7);
            unsafe { o0.StoreUnsafe(ref block, (nuint)u); }
            unsafe { o1.StoreUnsafe(ref block, (nuint)(8 + u)); }
            unsafe { o2.StoreUnsafe(ref block, (nuint)(16 + u)); }
            unsafe { o3.StoreUnsafe(ref block, (nuint)(24 + u)); }
            unsafe { o4.StoreUnsafe(ref block, (nuint)(32 + u)); }
            unsafe { o5.StoreUnsafe(ref block, (nuint)(40 + u)); }
            unsafe { o6.StoreUnsafe(ref block, (nuint)(48 + u)); }
            unsafe { o7.StoreUnsafe(ref block, (nuint)(56 + u)); }
        }

        // Transpose, so that the row pass also loads contiguous vectors: block[u * 8 + y]
        for (var i = 0; i < 8; i++)
        {
            for (var j = i + 1; j < 8; j++)
            {
                ref var a = ref unsafe(Unsafe.Add(ref block, (i * 8) + j));
                ref var b = ref unsafe(Unsafe.Add(ref block, (j * 8) + i));
                (a, b) = (b, a);
            }
        }

        // Rows, count at a time: lane k of vector u holds sample u of row y + k
        for (var y = 0; y < 8; y += count)
        {
            Transform1D(
                Load(ref block, y), Load(ref block, 8 + y), Load(ref block, 16 + y), Load(ref block, 24 + y),
                Load(ref block, 32 + y), Load(ref block, 40 + y), Load(ref block, 48 + y), Load(ref block, 56 + y),
                out var o0, out var o1, out var o2, out var o3, out var o4, out var o5, out var o6, out var o7);
            ref var row = ref unsafe(Unsafe.Add(ref output, y * stride));
            Store(o0, ref row, stride);
            Store(o1, ref unsafe(Unsafe.Add(ref row, 1)), stride);
            Store(o2, ref unsafe(Unsafe.Add(ref row, 2)), stride);
            Store(o3, ref unsafe(Unsafe.Add(ref row, 3)), stride);
            Store(o4, ref unsafe(Unsafe.Add(ref row, 4)), stride);
            Store(o5, ref unsafe(Unsafe.Add(ref row, 5)), stride);
            Store(o6, ref unsafe(Unsafe.Add(ref row, 6)), stride);
            Store(o7, ref unsafe(Unsafe.Add(ref row, 7)), stride);
        }

        static Vector<double> Load(ref double block, int index) => unsafe(Vector.LoadUnsafe(ref block, (nuint)index));
    }

    /// <summary>Writes one sample of <see cref="Vector{T}.Count"/> consecutive rows: <c>floor(x + 128.5)</c> clamped to 0..255, as <see cref="ToSample"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Store(Vector<double> value, ref byte destination, int stride)
    {
        var samples = Vector.ConvertToInt64(Vector.Min(Vector.Max(Vector.Floor(value + new Vector<double>(128.5)), Vector<double>.Zero), new Vector<double>(255)));
        for (var k = 0; k < Vector<double>.Count; k++)
        {
            unsafe { Unsafe.Add(ref destination, k * stride) = (byte)samples.GetElement(k); }
        }
    }

    /// <summary>The vector form of <see cref="Transform1D(double, double, double, double, double, double, double, double, out double, out double, out double, out double, out double, out double, out double, out double)"/>: the same expressions, lane by lane.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Transform1D(Vector<double> x0, Vector<double> x1, Vector<double> x2, Vector<double> x3, Vector<double> x4, Vector<double> x5, Vector<double> x6, Vector<double> x7,
        out Vector<double> o0, out Vector<double> o1, out Vector<double> o2, out Vector<double> o3, out Vector<double> o4, out Vector<double> o5, out Vector<double> o6, out Vector<double> o7)
    {
        // Even part: x0, x2, x4, x6
        var a = C0 * (x0 + x4);
        var b = C0 * (x0 - x4);
        var p = (C2 * x2) + (C6 * x6);
        var q = (C6 * x2) - (C2 * x6);
        var e0 = a + p;
        var e1 = b + q;
        var e2 = b - q;
        var e3 = a - p;

        // Odd part: x1, x3, x5, x7
        var d0 = (C1 * x1) + (C3 * x3) + (C5 * x5) + (C7 * x7);
        var d1 = (C3 * x1) - (C7 * x3) - (C1 * x5) - (C5 * x7);
        var d2 = (C5 * x1) - (C1 * x3) + (C7 * x5) + (C3 * x7);
        var d3 = (C7 * x1) - (C5 * x3) + (C3 * x5) - (C1 * x7);

        o0 = e0 + d0;
        o7 = e0 - d0;
        o1 = e1 + d1;
        o6 = e1 - d1;
        o2 = e2 + d2;
        o5 = e2 - d2;
        o3 = e3 + d3;
        o4 = e3 - d3;
    }

    /// <summary>
    /// One 8-point inverse DCT: <c>o[n] = sum over k of C(k)/2 * x[k] * cos((2n + 1) * k * pi / 16)</c>, with
    /// <c>o[n] = even[n] + odd[n]</c> and <c>o[7 - n] = even[n] - odd[n]</c>.
    /// </summary>
    private static void Transform1D(double x0, double x1, double x2, double x3, double x4, double x5, double x6, double x7,
        out double o0, out double o1, out double o2, out double o3, out double o4, out double o5, out double o6, out double o7)
    {
        // Even part: x0, x2, x4, x6
        var a = C0 * (x0 + x4);
        var b = C0 * (x0 - x4);
        var p = (C2 * x2) + (C6 * x6);
        var q = (C6 * x2) - (C2 * x6);
        var e0 = a + p;
        var e1 = b + q;
        var e2 = b - q;
        var e3 = a - p;

        // Odd part: x1, x3, x5, x7
        var d0 = (C1 * x1) + (C3 * x3) + (C5 * x5) + (C7 * x7);
        var d1 = (C3 * x1) - (C7 * x3) - (C1 * x5) - (C5 * x7);
        var d2 = (C5 * x1) - (C1 * x3) + (C7 * x5) + (C3 * x7);
        var d3 = (C7 * x1) - (C5 * x3) + (C3 * x5) - (C1 * x7);

        o0 = e0 + d0;
        o7 = e0 - d0;
        o1 = e1 + d1;
        o6 = e1 - d1;
        o2 = e2 + d2;
        o5 = e2 - d2;
        o3 = e3 + d3;
        o4 = e3 - d3;
    }

    private static byte ToSample(double value)
    {
        var rounded = Math.Floor(value + 128.5);
        return rounded <= 0 ? (byte)0 : rounded >= 255 ? (byte)255 : (byte)rounded;
    }
}
