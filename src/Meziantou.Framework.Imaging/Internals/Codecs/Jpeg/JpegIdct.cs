using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The 8x8 inverse DCT of the JPEG decoders (ITU-T T.81 section A.3.3), with this numerical contract:
/// a separable transform in IEEE double precision (columns first, then rows; each 8-point pass uses the
/// even/odd decomposition with the constants <c>C(k)/2 * cos(k*pi/16)</c> written below as literals, so results do not depend
/// on the platform's <c>Math.Cos</c>), the level shift +128, rounding to nearest with ties upward (<c>floor(x + 0.5)</c>),
/// and clamping to 0..255. A block whose AC coefficients are all zero is computed exactly in integers:
/// <c>floor((DC * Q + 4) / 8) + 128</c>, clamped. Samples of 12 bits follow the same contract with the level shift +2048
/// and the range 0..4095.
/// </summary>
/// <remarks>
/// <para>
/// With hardware vector support, <see cref="Transform(Span{double}, Span{byte}, int, bool)"/> runs each pass on two
/// columns (then rows) at once, four with AVX2, or on <see cref="Vector{T}.Count"/> of them with other wider vectors: every lane performs exactly
/// the operations of the scalar reference (<see cref="TransformScalar"/>) in the same order, without fused multiply-adds,
/// so the samples are bit-identical. The scalar shortcut for columns without AC coefficients is skipped there; it is
/// itself exact (the full formula reduces to <c>x0 * C0</c> when the other inputs are zero). The transform of 12-bit
/// samples is the scalar one.
/// </para>
/// <para>
/// A block whose non-zero coefficients are all in its top-left 4x4 corner (the first 10 in zig-zag order, and the others
/// of that corner) skips the columns 4 to 7, whose transform is zero, and drops the inputs 4 to 7, which are zero, from
/// both passes (<c>Transform1D</c> of four inputs). This is exact: a term multiplied by zero is a zero, and adding a zero
/// to a value gives the value itself; the only difference can be the sign of a zero result, which stays a zero through
/// the additions and multiplications that follow and gives the same sample (<c>floor(±0 + 128.5)</c>).
/// </para>
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

    /// <summary>Writes the samples of more than 8 bits of a block whose AC coefficients are all zero.</summary>
    /// <param name="dequantizedDc">The DC coefficient multiplied by its quantization value.</param>
    /// <param name="destination">The first sample of the block.</param>
    /// <param name="stride">The distance between two sample rows.</param>
    /// <param name="precision">The number of bits of a sample.</param>
    public static void TransformDeepDcOnly(long dequantizedDc, Span<ushort> destination, int stride, int precision)
    {
        var value = (ushort)Math.Clamp(((dequantizedDc + 4) >> 3) + (1 << (precision - 1)), 0, (1 << precision) - 1);
        for (var y = 0; y < 8; y++)
        {
            destination.Slice(y * stride, 8).Fill(value);
        }
    }

    /// <summary>Transforms dequantized coefficients (natural order) into 8x8 samples of more than 8 bits.</summary>
    /// <param name="coefficients">64 dequantized coefficients, natural order; overwritten (used as the intermediate buffer).</param>
    /// <param name="destination">The first sample of the block.</param>
    /// <param name="stride">The distance between two sample rows.</param>
    /// <param name="precision">The number of bits of a sample.</param>
    public static void TransformDeep(Span<double> coefficients, Span<ushort> destination, int stride, int precision)
    {
        var block = coefficients[..64];
        TransformColumns(block);

        // Rows: block[y * 8 + u] -> samples, level shifted and rounded like the 8-bit samples
        var shift = (1 << (precision - 1)) + 0.5;
        double max = (1 << precision) - 1;
        Span<double> samples = stackalloc double[8];
        for (var y = 0; y < 8; y++)
        {
            var row = block.Slice(y * 8, 8);
            Transform1D(row[0], row[1], row[2], row[3], row[4], row[5], row[6], row[7], out samples[0], out samples[1], out samples[2], out samples[3], out samples[4], out samples[5], out samples[6], out samples[7]);
            var output = destination.Slice(y * stride, 8);
            for (var x = 0; x < 8; x++)
            {
                var rounded = Math.Floor(samples[x] + shift);
                output[x] = rounded <= 0 ? (ushort)0 : rounded >= max ? (ushort)max : (ushort)rounded;
            }
        }
    }

    /// <summary>Transforms dequantized coefficients (natural order) into 8x8 samples.</summary>
    /// <param name="coefficients">64 dequantized coefficients, natural order; overwritten (used as the intermediate buffer).</param>
    /// <param name="destination">The first sample of the block.</param>
    /// <param name="stride">The distance between two sample rows.</param>
    public static void Transform(Span<double> coefficients, Span<byte> destination, int stride) => Transform(coefficients, destination, stride, sparse: false);

    /// <summary>Transforms dequantized coefficients (natural order) into 8x8 samples.</summary>
    /// <param name="coefficients">64 dequantized coefficients, natural order; overwritten (used as the intermediate buffer).</param>
    /// <param name="destination">The first sample of the block.</param>
    /// <param name="stride">The distance between two sample rows.</param>
    /// <param name="sparse">Whether every coefficient outside the top-left 4x4 corner is zero (see <see cref="IsSparse(int)"/>).</param>
    public static void Transform(Span<double> coefficients, Span<byte> destination, int stride, bool sparse)
    {
        if (Avx2.IsSupported)
        {
            TransformAvx2(coefficients, destination, stride, sparse);
        }
        else if (Vector128.IsHardwareAccelerated && Vector<double>.Count == 2)
        {
            TransformVector128(coefficients, destination, stride, sparse);
        }
        else if (Vector.IsHardwareAccelerated && Vector<double>.Count is 2 or 4 or 8)
        {
            TransformVector(coefficients, destination, stride, sparse);
        }
        else
        {
            TransformScalar(coefficients, destination, stride);
        }
    }

    /// <summary>Dequantizes the coefficients of a block: <c>block[i] = coefficients[i] * (double)quantization[i]</c>, exactly.</summary>
    /// <param name="coefficients">The 64 quantized coefficients.</param>
    /// <param name="quantization">The 64 quantization values, as doubles.</param>
    /// <param name="block">The 64 dequantized coefficients.</param>
    /// <remarks>
    /// With 128-bit hardware vectors, the coefficients are widened and converted two at a time (four with AVX2, from 32-bit
    /// integers on x64, which has no 64-bit integer conversion before AVX-512); every conversion is exact and the product
    /// is the same IEEE multiplication, so the values are identical.
    /// </remarks>
    public static void Dequantize(ReadOnlySpan<short> coefficients, ReadOnlySpan<double> quantization, Span<double> block)
    {
        if (Avx2.IsSupported)
        {
            DequantizeAvx2(coefficients, quantization, block);
        }
        else if (Sse2.IsSupported)
        {
            DequantizeSse2(coefficients, quantization, block);
        }
        else if (Vector128.IsHardwareAccelerated)
        {
            DequantizeVector128(coefficients, quantization, block);
        }
        else
        {
            DequantizeScalar(coefficients, quantization, block);
        }
    }

    /// <summary>The dequantization with <see cref="Vector128{T}"/>: the coefficients are widened to 64-bit integers, then converted two at a time.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void DequantizeVector128(ReadOnlySpan<short> coefficients, ReadOnlySpan<double> quantization, Span<double> block)
    {
        // Bounds are checked once here; the loop below uses unchecked references
        ref var source = ref unsafe(MemoryMarshal.GetReference(coefficients[..64]));
        ref var scales = ref unsafe(MemoryMarshal.GetReference(quantization[..64]));
        ref var destination = ref unsafe(MemoryMarshal.GetReference(block[..64]));
        for (var i = 0; i < 64; i += 8)
        {
            var (low, high) = Vector128.Widen(unsafe(Vector128.LoadUnsafe(ref source, (nuint)i)));
            Store(Vector128.WidenLower(low), ref scales, ref destination, i);
            Store(Vector128.WidenUpper(low), ref scales, ref destination, i + 2);
            Store(Vector128.WidenLower(high), ref scales, ref destination, i + 4);
            Store(Vector128.WidenUpper(high), ref scales, ref destination, i + 6);
        }

        static void Store(Vector128<long> values, ref double scales, ref double destination, int index)
        {
            var product = Vector128.ConvertToDouble(values) * unsafe(Vector128.LoadUnsafe(ref scales, (nuint)index));
            unsafe { product.StoreUnsafe(ref destination, (nuint)index); }
        }
    }

    /// <summary>The dequantization with AVX2: eight coefficients widened to 32-bit integers, then converted four at a time.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void DequantizeAvx2(ReadOnlySpan<short> coefficients, ReadOnlySpan<double> quantization, Span<double> block)
    {
        ref var source = ref unsafe(MemoryMarshal.GetReference(coefficients[..64]));
        ref var scales = ref unsafe(MemoryMarshal.GetReference(quantization[..64]));
        ref var destination = ref unsafe(MemoryMarshal.GetReference(block[..64]));
        for (var i = 0; i < 64; i += 8)
        {
            var integers = Avx2.ConvertToVector256Int32(unsafe(Vector128.LoadUnsafe(ref source, (nuint)i)));
            var low = Avx.ConvertToVector256Double(integers.GetLower()) * unsafe(Vector256.LoadUnsafe(ref scales, (nuint)i));
            var high = Avx.ConvertToVector256Double(integers.GetUpper()) * unsafe(Vector256.LoadUnsafe(ref scales, (nuint)(i + 4)));
            unsafe { low.StoreUnsafe(ref destination, (nuint)i); }
            unsafe { high.StoreUnsafe(ref destination, (nuint)(i + 4)); }
        }
    }

    /// <summary>The dequantization with SSE2: the coefficients are widened to 32-bit integers, then converted two at a time.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void DequantizeSse2(ReadOnlySpan<short> coefficients, ReadOnlySpan<double> quantization, Span<double> block)
    {
        ref var source = ref unsafe(MemoryMarshal.GetReference(coefficients[..64]));
        ref var scales = ref unsafe(MemoryMarshal.GetReference(quantization[..64]));
        ref var destination = ref unsafe(MemoryMarshal.GetReference(block[..64]));
        for (var i = 0; i < 64; i += 8)
        {
            var (low, high) = Vector128.Widen(unsafe(Vector128.LoadUnsafe(ref source, (nuint)i)));
            Store(low, ref scales, ref destination, i);
            Store(high, ref scales, ref destination, i + 4);
        }

        // CVTDQ2PD converts the two lower integers
        static void Store(Vector128<int> values, ref double scales, ref double destination, int index)
        {
            var low = Sse2.ConvertToVector128Double(values) * unsafe(Vector128.LoadUnsafe(ref scales, (nuint)index));
            var high = Sse2.ConvertToVector128Double(Sse2.UnpackHigh(values.AsInt64(), values.AsInt64()).AsInt32()) * unsafe(Vector128.LoadUnsafe(ref scales, (nuint)(index + 2)));
            unsafe { low.StoreUnsafe(ref destination, (nuint)index); }
            unsafe { high.StoreUnsafe(ref destination, (nuint)(index + 2)); }
        }
    }

    /// <summary>The scalar reference of <see cref="Dequantize"/>.</summary>
    internal static void DequantizeScalar(ReadOnlySpan<short> coefficients, ReadOnlySpan<double> quantization, Span<double> block)
    {
        for (var i = 0; i < 64; i++)
        {
            block[i] = coefficients[i] * quantization[i];
        }
    }

    /// <summary>
    /// Gets a value indicating whether the natural indexes of the non-zero coefficients of a block, ORed together, are all in
    /// the top-left 4x4 corner: row and column below 4 (bits 5 and 2 clear).
    /// </summary>
    /// <param name="naturalIndexes">The natural indexes of the non-zero coefficients, ORed together.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsSparse(int naturalIndexes) => (naturalIndexes & 0b100_100) == 0;

    /// <summary>Gets a value indicating whether every coefficient (natural order) outside the top-left 4x4 corner of a block is zero.</summary>
    /// <param name="coefficients">The 64 coefficients.</param>
    public static bool IsSparse(ReadOnlySpan<short> coefficients)
    {
        // Rows 0 to 3: the columns 4 to 7 are the second half of each row; rows 4 to 7: everything
        var words = unsafe(MemoryMarshal.Cast<short, ulong>(coefficients[..64]));
        return (words[1] | words[3] | words[5] | words[7]) == 0 && words[8..].IndexOfAnyExcept(0UL) < 0;
    }

    /// <summary>The scalar reference of <see cref="Transform(Span{double}, Span{byte}, int, bool)"/>.</summary>
    internal static void TransformScalar(Span<double> coefficients, Span<byte> destination, int stride)
    {
        var block = coefficients[..64];
        TransformColumns(block);

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

    /// <summary>The column pass of the scalar transform: <c>block[v * 8 + u]</c> becomes <c>block[y * 8 + u]</c>.</summary>
    private static void TransformColumns(Span<double> block)
    {
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
    }

    /// <summary>The transform with <see cref="Vector128{T}"/>: two columns, then two rows at once, the rows read from the column pass with interleaving instead of a transposition.</summary>
    internal static void TransformVector128(Span<double> coefficients, Span<byte> destination, int stride, bool sparse)
    {
        // Bounds are checked once here; the loops below use unchecked references
        ArgumentOutOfRangeException.ThrowIfLessThan(coefficients.Length, 64);
        ArgumentOutOfRangeException.ThrowIfLessThan(destination.Length, (7 * stride) + 8);
        ref var block = ref unsafe(MemoryMarshal.GetReference(coefficients));
        ref var output = ref unsafe(MemoryMarshal.GetReference(destination));

        if (sparse)
        {
            // Columns 0 to 3 (rows 4 to 7 are zero); the columns 4 to 7 are zero and stay so
            for (var u = 0; u < 4; u += 2)
            {
                Transform1D(
                    Load128(ref block, u), Load128(ref block, 8 + u), Load128(ref block, 16 + u), Load128(ref block, 24 + u),
                    out var o0, out var o1, out var o2, out var o3, out var o4, out var o5, out var o6, out var o7);
                Store128(o0, o1, o2, o3, o4, o5, o6, o7, ref block, u);
            }

            // Rows, two at a time (the samples 4 to 7 of each row are zero): lane 0 is row y, lane 1 row y + 1
            for (var y = 0; y < 8; y += 2)
            {
                var a0 = Load128(ref block, y * 8);
                var a2 = Load128(ref block, (y * 8) + 2);
                var b0 = Load128(ref block, (y * 8) + 8);
                var b2 = Load128(ref block, (y * 8) + 10);
                Transform1D(
                    InterleaveLower(a0, b0), InterleaveUpper(a0, b0), InterleaveLower(a2, b2), InterleaveUpper(a2, b2),
                    out var o0, out var o1, out var o2, out var o3, out var o4, out var o5, out var o6, out var o7);
                StoreSamples(o0, o1, o2, o3, o4, o5, o6, o7, ref unsafe(Unsafe.Add(ref output, y * stride)), stride);
            }

            return;
        }

        // Columns, two at a time: block[v * 8 + u] -> block[y * 8 + u]
        for (var u = 0; u < 8; u += 2)
        {
            Transform1D(
                Load128(ref block, u), Load128(ref block, 8 + u), Load128(ref block, 16 + u), Load128(ref block, 24 + u),
                Load128(ref block, 32 + u), Load128(ref block, 40 + u), Load128(ref block, 48 + u), Load128(ref block, 56 + u),
                out var o0, out var o1, out var o2, out var o3, out var o4, out var o5, out var o6, out var o7);
            Store128(o0, o1, o2, o3, o4, o5, o6, o7, ref block, u);
        }

        // Rows, two at a time: lane 0 is row y, lane 1 row y + 1
        for (var y = 0; y < 8; y += 2)
        {
            var a0 = Load128(ref block, y * 8);
            var a2 = Load128(ref block, (y * 8) + 2);
            var a4 = Load128(ref block, (y * 8) + 4);
            var a6 = Load128(ref block, (y * 8) + 6);
            var b0 = Load128(ref block, (y * 8) + 8);
            var b2 = Load128(ref block, (y * 8) + 10);
            var b4 = Load128(ref block, (y * 8) + 12);
            var b6 = Load128(ref block, (y * 8) + 14);
            Transform1D(
                InterleaveLower(a0, b0), InterleaveUpper(a0, b0), InterleaveLower(a2, b2), InterleaveUpper(a2, b2),
                InterleaveLower(a4, b4), InterleaveUpper(a4, b4), InterleaveLower(a6, b6), InterleaveUpper(a6, b6),
                out var o0, out var o1, out var o2, out var o3, out var o4, out var o5, out var o6, out var o7);
            StoreSamples(o0, o1, o2, o3, o4, o5, o6, o7, ref unsafe(Unsafe.Add(ref output, y * stride)), stride);
        }

        static Vector128<double> Load128(ref double block, int index) => unsafe(Vector128.LoadUnsafe(ref block, (nuint)index));
    }

    /// <summary>Stores the outputs of a column pass: <c>o[y]</c> at <c>block[y * 8 + u]</c>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Store128(Vector128<double> o0, Vector128<double> o1, Vector128<double> o2, Vector128<double> o3, Vector128<double> o4, Vector128<double> o5, Vector128<double> o6, Vector128<double> o7, ref double block, int u)
    {
        unsafe { o0.StoreUnsafe(ref block, (nuint)u); }
        unsafe { o1.StoreUnsafe(ref block, (nuint)(8 + u)); }
        unsafe { o2.StoreUnsafe(ref block, (nuint)(16 + u)); }
        unsafe { o3.StoreUnsafe(ref block, (nuint)(24 + u)); }
        unsafe { o4.StoreUnsafe(ref block, (nuint)(32 + u)); }
        unsafe { o5.StoreUnsafe(ref block, (nuint)(40 + u)); }
        unsafe { o6.StoreUnsafe(ref block, (nuint)(48 + u)); }
        unsafe { o7.StoreUnsafe(ref block, (nuint)(56 + u)); }
    }

    /// <summary>
    /// Writes two rows of samples: <c>o[n]</c> holds the sample <c>n</c> of the first row in lane 0 and of the second in lane 1.
    /// Each value is rounded and clamped as <see cref="ToSample"/> does, the lanes are regrouped by row, and each row is
    /// narrowed to 8 bytes and written at once.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void StoreSamples(Vector128<double> o0, Vector128<double> o1, Vector128<double> o2, Vector128<double> o3, Vector128<double> o4, Vector128<double> o5, Vector128<double> o6, Vector128<double> o7, ref byte destination, int stride)
    {
        if (Sse2.IsSupported)
        {
            StoreSamplesSse2(o0, o1, o2, o3, o4, o5, o6, o7, ref destination, stride);
            return;
        }

        var r0 = RoundAndClamp(o0);
        var r1 = RoundAndClamp(o1);
        var r2 = RoundAndClamp(o2);
        var r3 = RoundAndClamp(o3);
        var r4 = RoundAndClamp(o4);
        var r5 = RoundAndClamp(o5);
        var r6 = RoundAndClamp(o6);
        var r7 = RoundAndClamp(o7);
        var first = Vector128.Narrow(
            Vector128.Narrow(ToUInt64(InterleaveLower(r0, r1)), ToUInt64(InterleaveLower(r2, r3))),
            Vector128.Narrow(ToUInt64(InterleaveLower(r4, r5)), ToUInt64(InterleaveLower(r6, r7))));
        var second = Vector128.Narrow(
            Vector128.Narrow(ToUInt64(InterleaveUpper(r0, r1)), ToUInt64(InterleaveUpper(r2, r3))),
            Vector128.Narrow(ToUInt64(InterleaveUpper(r4, r5)), ToUInt64(InterleaveUpper(r6, r7))));
        var rows = Vector128.Narrow(first, second).AsUInt64();
        unsafe { Unsafe.WriteUnaligned(ref destination, rows.ToScalar()); }
        unsafe { Unsafe.WriteUnaligned(ref Unsafe.Add(ref destination, stride), rows.GetElement(1)); }

        static Vector128<double> RoundAndClamp(Vector128<double> value) => Vector128.Min(Vector128.Max(Vector128.Floor(value + Vector128.Create(128.5)), Vector128<double>.Zero), Vector128.Create(255.0));

        // The values are integers in 0..255: the conversion is exact
        static Vector128<ulong> ToUInt64(Vector128<double> value) => Vector128.ConvertToInt64(value).AsUInt64();
    }

    /// <summary>
    /// The x64 form of <see cref="StoreSamples"/>, which has no 64-bit integer conversion before AVX-512. The rounded values
    /// are clamped to 255 only: CVTTPD2DQ converts them exactly (a value below the range of 32-bit integers becomes
    /// <see cref="int.MinValue"/>), and the saturating packs clamp the negative ones to 0, as <see cref="ToSample"/> does.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void StoreSamplesSse2(Vector128<double> o0, Vector128<double> o1, Vector128<double> o2, Vector128<double> o3, Vector128<double> o4, Vector128<double> o5, Vector128<double> o6, Vector128<double> o7, ref byte destination, int stride)
    {
        var r0 = Round(o0);
        var r1 = Round(o1);
        var r2 = Round(o2);
        var r3 = Round(o3);
        var r4 = Round(o4);
        var r5 = Round(o5);
        var r6 = Round(o6);
        var r7 = Round(o7);
        var first = Sse2.PackSignedSaturate(
            Combine(Sse2.UnpackLow(r0, r1), Sse2.UnpackLow(r2, r3)),
            Combine(Sse2.UnpackLow(r4, r5), Sse2.UnpackLow(r6, r7)));
        var second = Sse2.PackSignedSaturate(
            Combine(Sse2.UnpackHigh(r0, r1), Sse2.UnpackHigh(r2, r3)),
            Combine(Sse2.UnpackHigh(r4, r5), Sse2.UnpackHigh(r6, r7)));
        var rows = Sse2.PackUnsignedSaturate(first, second).AsUInt64();
        unsafe { Unsafe.WriteUnaligned(ref destination, rows.ToScalar()); }
        unsafe { Unsafe.WriteUnaligned(ref Unsafe.Add(ref destination, stride), rows.GetElement(1)); }

        static Vector128<double> Round(Vector128<double> value) => Sse2.Min(Vector128.Floor(value + Vector128.Create(128.5)), Vector128.Create(255.0));

        // The four integers of two pairs: CVTTPD2DQ writes its two results in the lower half
        static Vector128<int> Combine(Vector128<double> low, Vector128<double> high) =>
            Sse2.UnpackLow(Sse2.ConvertToVector128Int32WithTruncation(low).AsInt64(), Sse2.ConvertToVector128Int32WithTruncation(high).AsInt64()).AsInt32();
    }

    /// <summary>Gets the lanes 0 of two vectors: <c>(a[0], b[0])</c>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Vector128<double> InterleaveLower(Vector128<double> a, Vector128<double> b)
    {
        if (AdvSimd.Arm64.IsSupported)
            return AdvSimd.Arm64.ZipLow(a, b);

        if (Sse2.IsSupported)
            return Sse2.UnpackLow(a, b);

        return Vector128.Create(a.ToScalar(), b.ToScalar());
    }

    /// <summary>Gets the lanes 1 of two vectors: <c>(a[1], b[1])</c>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Vector128<double> InterleaveUpper(Vector128<double> a, Vector128<double> b)
    {
        if (AdvSimd.Arm64.IsSupported)
            return AdvSimd.Arm64.ZipHigh(a, b);

        if (Sse2.IsSupported)
            return Sse2.UnpackHigh(a, b);

        return Vector128.Create(a.GetElement(1), b.GetElement(1));
    }

    /// <summary>
    /// The transform with AVX2: four columns, then four rows at once. The row pass reads each 4x4 quadrant of the column
    /// pass transposed in registers, so that lane <c>k</c> holds row <c>y + k</c>, and the samples are converted to 32-bit
    /// integers (x64 has no 64-bit integer conversion before AVX-512), packed to bytes and regrouped by row.
    /// </summary>
    internal static void TransformAvx2(Span<double> coefficients, Span<byte> destination, int stride, bool sparse)
    {
        // Bounds are checked once here; the loops below use unchecked references
        ArgumentOutOfRangeException.ThrowIfLessThan(coefficients.Length, 64);
        ArgumentOutOfRangeException.ThrowIfLessThan(destination.Length, (7 * stride) + 8);
        ref var block = ref unsafe(MemoryMarshal.GetReference(coefficients));
        ref var output = ref unsafe(MemoryMarshal.GetReference(destination));

        if (sparse)
        {
            // Columns 0 to 3 (rows 4 to 7 are zero); the columns 4 to 7 are zero and stay so
            Transform1D(
                Load256(ref block, 0), Load256(ref block, 8), Load256(ref block, 16), Load256(ref block, 24),
                out var c0, out var c1, out var c2, out var c3, out var c4, out var c5, out var c6, out var c7);
            Store256(c0, c1, c2, c3, c4, c5, c6, c7, ref block, 0);

            // Rows, four at a time (the samples 4 to 7 of each row are zero)
            for (var y = 0; y < 8; y += 4)
            {
                Transpose(Load256(ref block, y * 8), Load256(ref block, (y * 8) + 8), Load256(ref block, (y * 8) + 16), Load256(ref block, (y * 8) + 24), out var x0, out var x1, out var x2, out var x3);
                Transform1D(x0, x1, x2, x3, out var o0, out var o1, out var o2, out var o3, out var o4, out var o5, out var o6, out var o7);
                StoreSamplesAvx2(o0, o1, o2, o3, o4, o5, o6, o7, ref unsafe(Unsafe.Add(ref output, y * stride)), stride);
            }

            return;
        }

        // Columns, four at a time: block[v * 8 + u] -> block[y * 8 + u]
        for (var u = 0; u < 8; u += 4)
        {
            Transform1D(
                Load256(ref block, u), Load256(ref block, 8 + u), Load256(ref block, 16 + u), Load256(ref block, 24 + u),
                Load256(ref block, 32 + u), Load256(ref block, 40 + u), Load256(ref block, 48 + u), Load256(ref block, 56 + u),
                out var o0, out var o1, out var o2, out var o3, out var o4, out var o5, out var o6, out var o7);
            Store256(o0, o1, o2, o3, o4, o5, o6, o7, ref block, u);
        }

        // Rows, four at a time: lane k is row y + k
        for (var y = 0; y < 8; y += 4)
        {
            ref var rows = ref unsafe(Unsafe.Add(ref block, y * 8));
            Transpose(Load256(ref rows, 0), Load256(ref rows, 8), Load256(ref rows, 16), Load256(ref rows, 24), out var x0, out var x1, out var x2, out var x3);
            Transpose(Load256(ref rows, 4), Load256(ref rows, 12), Load256(ref rows, 20), Load256(ref rows, 28), out var x4, out var x5, out var x6, out var x7);
            Transform1D(x0, x1, x2, x3, x4, x5, x6, x7, out var o0, out var o1, out var o2, out var o3, out var o4, out var o5, out var o6, out var o7);
            StoreSamplesAvx2(o0, o1, o2, o3, o4, o5, o6, o7, ref unsafe(Unsafe.Add(ref output, y * stride)), stride);
        }

        static Vector256<double> Load256(ref double block, int index) => unsafe(Vector256.LoadUnsafe(ref block, (nuint)index));
    }

    /// <summary>Stores the outputs of a column pass: <c>o[y]</c> at <c>block[y * 8 + u]</c>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Store256(Vector256<double> o0, Vector256<double> o1, Vector256<double> o2, Vector256<double> o3, Vector256<double> o4, Vector256<double> o5, Vector256<double> o6, Vector256<double> o7, ref double block, int u)
    {
        unsafe { o0.StoreUnsafe(ref block, (nuint)u); }
        unsafe { o1.StoreUnsafe(ref block, (nuint)(8 + u)); }
        unsafe { o2.StoreUnsafe(ref block, (nuint)(16 + u)); }
        unsafe { o3.StoreUnsafe(ref block, (nuint)(24 + u)); }
        unsafe { o4.StoreUnsafe(ref block, (nuint)(32 + u)); }
        unsafe { o5.StoreUnsafe(ref block, (nuint)(40 + u)); }
        unsafe { o6.StoreUnsafe(ref block, (nuint)(48 + u)); }
        unsafe { o7.StoreUnsafe(ref block, (nuint)(56 + u)); }
    }

    /// <summary>Transposes a 4x4 matrix of doubles: <c>t[j][k] = r[k][j]</c>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void Transpose(Vector256<double> r0, Vector256<double> r1, Vector256<double> r2, Vector256<double> r3, out Vector256<double> t0, out Vector256<double> t1, out Vector256<double> t2, out Vector256<double> t3)
    {
        // (r0[0], r1[0], r0[2], r1[2]), (r0[1], r1[1], r0[3], r1[3]), and the same for r2 and r3
        var low01 = Avx.UnpackLow(r0, r1);
        var high01 = Avx.UnpackHigh(r0, r1);
        var low23 = Avx.UnpackLow(r2, r3);
        var high23 = Avx.UnpackHigh(r2, r3);
        t0 = Avx.Permute2x128(low01, low23, 0x20);
        t1 = Avx.Permute2x128(high01, high23, 0x20);
        t2 = Avx.Permute2x128(low01, low23, 0x31);
        t3 = Avx.Permute2x128(high01, high23, 0x31);
    }

    /// <summary>
    /// Writes four rows of samples: <c>o[n]</c> holds the sample <c>n</c> of the row <c>k</c> in lane <c>k</c>. The rounded
    /// values are clamped to 255 only: VCVTTPD2DQ converts them exactly (a value below the range of 32-bit integers becomes
    /// <see cref="int.MinValue"/>), and the saturating packs clamp the negative ones to 0, as <see cref="ToSample"/> does.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void StoreSamplesAvx2(Vector256<double> o0, Vector256<double> o1, Vector256<double> o2, Vector256<double> o3, Vector256<double> o4, Vector256<double> o5, Vector256<double> o6, Vector256<double> o7, ref byte destination, int stride)
    {
        // Byte n * 4 + k is the sample n of the row k, for n = 0..3 (then 4..7)
        var first = Sse2.PackUnsignedSaturate(Sse2.PackSignedSaturate(ToInt32(o0), ToInt32(o1)), Sse2.PackSignedSaturate(ToInt32(o2), ToInt32(o3)));
        var second = Sse2.PackUnsignedSaturate(Sse2.PackSignedSaturate(ToInt32(o4), ToInt32(o5)), Sse2.PackSignedSaturate(ToInt32(o6), ToInt32(o7)));

        // Byte k * 4 + n, then the two halves of each row together
        var byRow = Vector128.Create((byte)0, 4, 8, 12, 1, 5, 9, 13, 2, 6, 10, 14, 3, 7, 11, 15);
        first = Ssse3.Shuffle(first, byRow);
        second = Ssse3.Shuffle(second, byRow);
        var rows01 = Sse2.UnpackLow(first.AsInt32(), second.AsInt32()).AsUInt64();
        var rows23 = Sse2.UnpackHigh(first.AsInt32(), second.AsInt32()).AsUInt64();
        unsafe { Unsafe.WriteUnaligned(ref destination, rows01.ToScalar()); }
        unsafe { Unsafe.WriteUnaligned(ref Unsafe.Add(ref destination, stride), rows01.GetElement(1)); }
        unsafe { Unsafe.WriteUnaligned(ref Unsafe.Add(ref destination, 2 * stride), rows23.ToScalar()); }
        unsafe { Unsafe.WriteUnaligned(ref Unsafe.Add(ref destination, 3 * stride), rows23.GetElement(1)); }

        static Vector128<int> ToInt32(Vector256<double> value) => Avx.ConvertToVector128Int32WithTruncation(Avx.Min(Avx.Floor(value + Vector256.Create(128.5)), Vector256.Create(255.0)));
    }

    /// <summary>The transform with <see cref="Vector{T}"/>, <see cref="Vector{T}.Count"/> columns (then rows) at once.</summary>
    internal static void TransformVector(Span<double> coefficients, Span<byte> destination, int stride, bool sparse)
    {
        // Bounds are checked once here; the loops below use unchecked references
        ArgumentOutOfRangeException.ThrowIfLessThan(coefficients.Length, 64);
        ArgumentOutOfRangeException.ThrowIfLessThan(destination.Length, (7 * stride) + 8);
        ref var block = ref unsafe(MemoryMarshal.GetReference(coefficients));
        ref var output = ref unsafe(MemoryMarshal.GetReference(destination));
        var count = Vector<double>.Count;

        // Columns, count at a time: block[v * 8 + u] -> block[y * 8 + u]. A sparse block has the columns 0 to 3 only
        // (the others are zero and stay so) and zeros in the rows 4 to 7
        for (var u = 0; u < (sparse ? 4 : 8); u += count)
        {
            Vector<double> o0, o1, o2, o3, o4, o5, o6, o7;
            if (sparse)
            {
                Transform1D(
                    Load(ref block, u), Load(ref block, 8 + u), Load(ref block, 16 + u), Load(ref block, 24 + u),
                    out o0, out o1, out o2, out o3, out o4, out o5, out o6, out o7);
            }
            else
            {
                Transform1D(
                    Load(ref block, u), Load(ref block, 8 + u), Load(ref block, 16 + u), Load(ref block, 24 + u),
                    Load(ref block, 32 + u), Load(ref block, 40 + u), Load(ref block, 48 + u), Load(ref block, 56 + u),
                    out o0, out o1, out o2, out o3, out o4, out o5, out o6, out o7);
            }

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

        // Rows, count at a time: lane k of vector u holds sample u of row y + k (a sparse block has zeros in the samples 4 to 7)
        for (var y = 0; y < 8; y += count)
        {
            Vector<double> o0, o1, o2, o3, o4, o5, o6, o7;
            if (sparse)
            {
                Transform1D(
                    Load(ref block, y), Load(ref block, 8 + y), Load(ref block, 16 + y), Load(ref block, 24 + y),
                    out o0, out o1, out o2, out o3, out o4, out o5, out o6, out o7);
            }
            else
            {
                Transform1D(
                    Load(ref block, y), Load(ref block, 8 + y), Load(ref block, 16 + y), Load(ref block, 24 + y),
                    Load(ref block, 32 + y), Load(ref block, 40 + y), Load(ref block, 48 + y), Load(ref block, 56 + y),
                    out o0, out o1, out o2, out o3, out o4, out o5, out o6, out o7);
            }

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

    /// <summary>The <see cref="Vector128{T}"/> form of <see cref="Transform1D(double, double, double, double, double, double, double, double, out double, out double, out double, out double, out double, out double, out double, out double)"/>: the same expressions, lane by lane.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Transform1D(Vector128<double> x0, Vector128<double> x1, Vector128<double> x2, Vector128<double> x3, Vector128<double> x4, Vector128<double> x5, Vector128<double> x6, Vector128<double> x7,
        out Vector128<double> o0, out Vector128<double> o1, out Vector128<double> o2, out Vector128<double> o3, out Vector128<double> o4, out Vector128<double> o5, out Vector128<double> o6, out Vector128<double> o7)
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
    /// <see cref="Transform1D(Vector128{double}, Vector128{double}, Vector128{double}, Vector128{double}, Vector128{double}, Vector128{double}, Vector128{double}, Vector128{double}, out Vector128{double}, out Vector128{double}, out Vector128{double}, out Vector128{double}, out Vector128{double}, out Vector128{double}, out Vector128{double}, out Vector128{double})"/>
    /// with <c>x4 = x5 = x6 = x7 = 0</c>: the terms of the zero inputs are left out (see the remarks of the class).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Transform1D(Vector128<double> x0, Vector128<double> x1, Vector128<double> x2, Vector128<double> x3,
        out Vector128<double> o0, out Vector128<double> o1, out Vector128<double> o2, out Vector128<double> o3, out Vector128<double> o4, out Vector128<double> o5, out Vector128<double> o6, out Vector128<double> o7)
    {
        // Even part: a = b = C0 * x0, p = C2 * x2, q = C6 * x2
        var a = C0 * x0;
        var p = C2 * x2;
        var q = C6 * x2;
        var e0 = a + p;
        var e1 = a + q;
        var e2 = a - q;
        var e3 = a - p;

        // Odd part: the first two terms of each sum
        var d0 = (C1 * x1) + (C3 * x3);
        var d1 = (C3 * x1) - (C7 * x3);
        var d2 = (C5 * x1) - (C1 * x3);
        var d3 = (C7 * x1) - (C5 * x3);

        o0 = e0 + d0;
        o7 = e0 - d0;
        o1 = e1 + d1;
        o6 = e1 - d1;
        o2 = e2 + d2;
        o5 = e2 - d2;
        o3 = e3 + d3;
        o4 = e3 - d3;
    }

    /// <summary>The <see cref="Vector{T}"/> form of the four-input <c>Transform1D</c>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Transform1D(Vector<double> x0, Vector<double> x1, Vector<double> x2, Vector<double> x3,
        out Vector<double> o0, out Vector<double> o1, out Vector<double> o2, out Vector<double> o3, out Vector<double> o4, out Vector<double> o5, out Vector<double> o6, out Vector<double> o7)
    {
        var a = C0 * x0;
        var p = C2 * x2;
        var q = C6 * x2;
        var e0 = a + p;
        var e1 = a + q;
        var e2 = a - q;
        var e3 = a - p;
        var d0 = (C1 * x1) + (C3 * x3);
        var d1 = (C3 * x1) - (C7 * x3);
        var d2 = (C5 * x1) - (C1 * x3);
        var d3 = (C7 * x1) - (C5 * x3);
        o0 = e0 + d0;
        o7 = e0 - d0;
        o1 = e1 + d1;
        o6 = e1 - d1;
        o2 = e2 + d2;
        o5 = e2 - d2;
        o3 = e3 + d3;
        o4 = e3 - d3;
    }

    /// <summary>The <see cref="Vector256{T}"/> form of the eight-input <c>Transform1D</c>: the same expressions, lane by lane.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Transform1D(Vector256<double> x0, Vector256<double> x1, Vector256<double> x2, Vector256<double> x3, Vector256<double> x4, Vector256<double> x5, Vector256<double> x6, Vector256<double> x7,
        out Vector256<double> o0, out Vector256<double> o1, out Vector256<double> o2, out Vector256<double> o3, out Vector256<double> o4, out Vector256<double> o5, out Vector256<double> o6, out Vector256<double> o7)
    {
        var a = C0 * (x0 + x4);
        var b = C0 * (x0 - x4);
        var p = (C2 * x2) + (C6 * x6);
        var q = (C6 * x2) - (C2 * x6);
        var e0 = a + p;
        var e1 = b + q;
        var e2 = b - q;
        var e3 = a - p;
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

    /// <summary>The <see cref="Vector256{T}"/> form of the four-input <c>Transform1D</c>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Transform1D(Vector256<double> x0, Vector256<double> x1, Vector256<double> x2, Vector256<double> x3,
        out Vector256<double> o0, out Vector256<double> o1, out Vector256<double> o2, out Vector256<double> o3, out Vector256<double> o4, out Vector256<double> o5, out Vector256<double> o6, out Vector256<double> o7)
    {
        var a = C0 * x0;
        var p = C2 * x2;
        var q = C6 * x2;
        var e0 = a + p;
        var e1 = a + q;
        var e2 = a - q;
        var e3 = a - p;
        var d0 = (C1 * x1) + (C3 * x3);
        var d1 = (C3 * x1) - (C7 * x3);
        var d2 = (C5 * x1) - (C1 * x3);
        var d3 = (C7 * x1) - (C5 * x3);
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
