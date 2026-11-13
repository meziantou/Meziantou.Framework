using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The prediction samples of a block predicted from other pictures (Rec. ITU-T H.265, clauses 8.5.3.3.3 "Fractional sample
/// interpolation process" and 8.5.3.3.4 "Weighted sample prediction process").
/// </summary>
internal static class HevcInterPredictor
{
    // Table 8-11 - Specification of the luma interpolation filter coefficients fL[ p ] for each 1/4 fractional sample
    // position p
    private static ReadOnlySpan<sbyte> LumaFilter =>
    [
        0, 0, 0, 64, 0, 0, 0, 0,
        -1, 4, -10, 58, 17, -5, 1, 0,
        -1, 4, -11, 40, 40, -11, 4, -1,
        0, 1, -5, 17, 58, -10, 4, -1,
    ];

    // Table 8-12 - Specification of the chroma interpolation filter coefficients fC[ p ] for each 1/8 fractional sample
    // position p
    private static ReadOnlySpan<sbyte> ChromaFilter =>
    [
        0, 64, 0, 0,
        -2, 58, 10, -2,
        -4, 54, 16, -2,
        -6, 46, 28, -4,
        -4, 36, 36, -4,
        -4, 28, 46, -6,
        -2, 16, 54, -4,
        -2, 10, 58, -2,
    ];

    /// <summary>
    /// Derives the prediction samples of a block from a reference picture (clauses 8.5.3.3.3.2 "Luma sample interpolation
    /// process" and 8.5.3.3.3.3 "Chroma sample interpolation process"): <c>predSamplesLX</c>, with 14 bits of precision.
    /// The samples that the block refers to outside the reference picture are those of its closest edge.
    /// </summary>
    /// <param name="reference">The plane of the reference picture.</param>
    /// <param name="xInt">The horizontal position in the plane of the first sample of the block, in full samples.</param>
    /// <param name="yInt">The vertical position in the plane of the first sample of the block, in full samples.</param>
    /// <param name="xFrac">The horizontal offset: quarters of a sample for luma, eighths for chroma.</param>
    /// <param name="yFrac">The vertical offset: quarters of a sample for luma, eighths for chroma.</param>
    /// <param name="width">The width of the block: at most 64.</param>
    /// <param name="height">The height of the block: at most 64.</param>
    /// <param name="luma">Whether the 8-tap filter of the luma samples applies, rather than the 4-tap filter of the chroma samples.</param>
    /// <param name="bitDepth">The bit depth of the samples.</param>
    /// <param name="destination">The prediction samples: the rows of the block, <paramref name="width"/> values each.</param>
    /// <param name="intermediate">A working buffer of 71 rows of 64 values.</param>
    /// <param name="window">A working buffer of 71 rows of 71 samples.</param>
    public static void Predict(Av1Plane reference, int xInt, int yInt, int xFrac, int yFrac, int width, int height, bool luma, int bitDepth, Span<short> destination, Span<short> intermediate, Span<ushort> window)
    {
        var taps = luma ? 8 : 4;
        var before = (taps >> 1) - 1;
        var filters = luma ? LumaFilter : ChromaFilter;
        var marginX = xFrac is not 0 ? taps - 1 : 0;
        var marginY = yFrac is not 0 ? taps - 1 : 0;
        var x0 = xInt - (xFrac is not 0 ? before : 0);
        var y0 = yInt - (yFrac is not 0 ? before : 0);
        var windowWidth = width + marginX;
        var windowHeight = height + marginY;

        // The reference samples of the block and of its filters: those of the plane, or a copy when some are outside it
        ReadOnlySpan<ushort> source;
        int stride;
        if (x0 >= 0 && y0 >= 0 && x0 + windowWidth <= reference.Width && y0 + windowHeight <= reference.Height)
        {
            stride = reference.Stride;
            source = reference.Samples[((y0 * stride) + x0)..];
        }
        else
        {
            // Equations 8-228 and 8-229: the coordinates are clipped to the reference picture
            var samples = reference.Samples;
            var maxX = reference.Width - 1;
            var maxY = reference.Height - 1;
            stride = windowWidth;
            for (var y = 0; y < windowHeight; y++)
            {
                var row = samples.Slice(Math.Clamp(y0 + y, 0, maxY) * reference.Stride, reference.Width);
                var target = window.Slice(y * windowWidth, windowWidth);
                for (var x = 0; x < windowWidth; x++)
                {
                    target[x] = row[Math.Clamp(x0 + x, 0, maxX)];
                }
            }

            source = window;
        }

        // 8 samples at a time with the 32-bit sums of the scalar path, then 4 columns of two rows at a time with the
        // widening products of 16-bit lanes, then the columns after the last multiple of 4
        var widening = (AdvSimd.Arm64.IsSupported || Sse41.IsSupported) && bitDepth <= 15;
        var vectorWidth = Vector128.IsHardwareAccelerated ? width & ~7 : 0;
        var quadWidth = widening ? width & ~3 : vectorWidth;
        if (quadWidth is 0)
        {
            Interpolate(source, stride, xFrac, yFrac, width, height, taps, filters, bitDepth, destination, intermediate, 0);
            return;
        }

        var shift1 = Math.Min(4, bitDepth - 8);
        var shift3 = 14 - bitDepth;
        var quad = quadWidth > vectorWidth;
        if (xFrac is 0 && yFrac is 0)
        {
            for (var y = 0; y < height; y++)
            {
                var row = source.Slice(y * stride, width);
                var target = destination.Slice(y * width, width);
                for (var x = 0; x < vectorWidth; x += 8)
                {
                    (Vector128.Create(row[x..]) << shift3).AsInt16().CopyTo(target[x..]);
                }

                if (quad)
                {
                    (Vector64.Create(row[vectorWidth..]) << shift3).AsInt16().CopyTo(target[vectorWidth..]);
                }
            }
        }
        else if (yFrac is 0)
        {
            var filter = filters.Slice(xFrac * taps, taps);
            if (widening && vectorWidth > 0)
            {
                FilterRowsWidening(source, stride, 1, filter, shift1, destination, width, vectorWidth, height);
            }
            else
            {
                for (var y = 0; y < height; y++)
                {
                    FilterRow(source.Slice(y * stride, width + taps - 1), 1, filter, shift1, destination.Slice(y * width, vectorWidth));
                }
            }

            if (quad)
            {
                FilterQuads(source[vectorWidth..], stride, 1, filter, shift1, destination[vectorWidth..], width, height);
            }
        }
        else if (xFrac is 0)
        {
            var filter = filters.Slice(yFrac * taps, taps);
            if (widening && vectorWidth > 0)
            {
                FilterRowsWidening(source, stride, stride, filter, shift1, destination, width, vectorWidth, height);
            }
            else
            {
                for (var y = 0; y < height; y++)
                {
                    FilterRow(source[(y * stride)..], stride, filter, shift1, destination.Slice(y * width, vectorWidth));
                }
            }

            if (quad)
            {
                FilterQuads(source[vectorWidth..], stride, stride, filter, shift1, destination[vectorWidth..], width, height);
            }
        }
        else
        {
            // The rows that the vertical filter reads are the output of the horizontal filter
            var horizontal = filters.Slice(xFrac * taps, taps);
            var vertical = filters.Slice(yFrac * taps, taps);
            if (widening && vectorWidth > 0)
            {
                FilterRowsWidening(source, stride, 1, horizontal, shift1, intermediate, 64, vectorWidth, windowHeight);
                FilterRowsWidening<short>(intermediate, 64, 64, vertical, 6, destination, width, vectorWidth, height);
            }
            else
            {
                for (var y = 0; y < windowHeight; y++)
                {
                    FilterRow(source.Slice(y * stride, windowWidth), 1, horizontal, shift1, intermediate.Slice(y * 64, vectorWidth));
                }

                for (var y = 0; y < height; y++)
                {
                    FilterRow(intermediate[(y * 64)..], 64, vertical, 6, destination.Slice(y * width, vectorWidth));
                }
            }

            if (quad)
            {
                FilterQuads(source[vectorWidth..], stride, 1, horizontal, shift1, intermediate[vectorWidth..], 64, windowHeight);
                FilterQuads<short>(intermediate[vectorWidth..], 64, 64, vertical, 6, destination[vectorWidth..], width, height);
            }
        }

        if (quadWidth < width)
        {
            Interpolate(source, stride, xFrac, yFrac, width, height, taps, filters, bitDepth, destination, intermediate, quadWidth);
        }
    }

    // The filter of a row of samples, 8 samples at a time: the sample x of the output is the sum of the products of the
    // taps by the samples x + i * step of the input
    private static void FilterRow(ReadOnlySpan<ushort> input, int step, ReadOnlySpan<sbyte> filter, int shift, Span<short> output)
    {
        for (var x = 0; x < output.Length; x += 8)
        {
            var sumLow = Vector128<int>.Zero;
            var sumHigh = Vector128<int>.Zero;
            for (var i = 0; i < filter.Length; i++)
            {
                var (low, high) = Vector128.Widen(Vector128.Create(input[(x + (i * step))..]));
                int tap = filter[i];
                sumLow += low.AsInt32() * tap;
                sumHigh += high.AsInt32() * tap;
            }

            Vector128.Narrow(sumLow >> shift, sumHigh >> shift).CopyTo(output[x..]);
        }
    }

    private static void FilterRow(ReadOnlySpan<short> input, int step, ReadOnlySpan<sbyte> filter, int shift, Span<short> output)
    {
        for (var x = 0; x < output.Length; x += 8)
        {
            var sumLow = Vector128<int>.Zero;
            var sumHigh = Vector128<int>.Zero;
            for (var i = 0; i < filter.Length; i++)
            {
                var (low, high) = Vector128.Widen(Vector128.Create(input[(x + (i * step))..]));
                int tap = filter[i];
                sumLow += low * tap;
                sumHigh += high * tap;
            }

            Vector128.Narrow(sumLow >> shift, sumHigh >> shift).CopyTo(output[x..]);
        }
    }

    // FilterRow of the first width samples (a multiple of 8) of rows of samples, with the products of 16-bit lanes added
    // into 32-bit lanes: the widening multiply-accumulate instructions of Arm64 (SMLAL, SMLAL2), or the multiply-add of
    // pairs of 16-bit lanes of x64 (PMADDWD) on the samples of two taps interleaved, 16 samples at a time with AVX2. The
    // products of a 16-bit sample by a tap and their sums fit in 32 bits: the sums are those of the scalar path, and so are
    // their low 16 bits
    private static void FilterRowsWidening<T>(ReadOnlySpan<T> input, int inputStride, int step, ReadOnlySpan<sbyte> filter, int shift, Span<short> output, int outputStride, int width, int rows)
        where T : unmanaged
    {
        var luma = filter.Length is 8;
        if (AdvSimd.Arm64.IsSupported)
        {
            var t0 = Vector128.Create((short)filter[0]);
            var t1 = Vector128.Create((short)filter[1]);
            var t2 = Vector128.Create((short)filter[2]);
            var t3 = Vector128.Create((short)filter[3]);
            var t4 = luma ? Vector128.Create((short)filter[4]) : default;
            var t5 = luma ? Vector128.Create((short)filter[5]) : default;
            var t6 = luma ? Vector128.Create((short)filter[6]) : default;
            var t7 = luma ? Vector128.Create((short)filter[7]) : default;
            for (var r = 0; r < rows; r++)
            {
                var row = input[(r * inputStride)..];
                var target = output.Slice(r * outputStride, width);
                for (var x = 0; x < width; x += 8)
                {
                    var v = Load128(row, x);
                    var low = AdvSimd.MultiplyWideningLower(v.GetLower(), t0.GetLower());
                    var high = AdvSimd.MultiplyWideningUpper(v, t0);
                    v = Load128(row, x + step);
                    low = AdvSimd.MultiplyWideningLowerAndAdd(low, v.GetLower(), t1.GetLower());
                    high = AdvSimd.MultiplyWideningUpperAndAdd(high, v, t1);
                    v = Load128(row, x + (2 * step));
                    low = AdvSimd.MultiplyWideningLowerAndAdd(low, v.GetLower(), t2.GetLower());
                    high = AdvSimd.MultiplyWideningUpperAndAdd(high, v, t2);
                    v = Load128(row, x + (3 * step));
                    low = AdvSimd.MultiplyWideningLowerAndAdd(low, v.GetLower(), t3.GetLower());
                    high = AdvSimd.MultiplyWideningUpperAndAdd(high, v, t3);
                    if (luma)
                    {
                        v = Load128(row, x + (4 * step));
                        low = AdvSimd.MultiplyWideningLowerAndAdd(low, v.GetLower(), t4.GetLower());
                        high = AdvSimd.MultiplyWideningUpperAndAdd(high, v, t4);
                        v = Load128(row, x + (5 * step));
                        low = AdvSimd.MultiplyWideningLowerAndAdd(low, v.GetLower(), t5.GetLower());
                        high = AdvSimd.MultiplyWideningUpperAndAdd(high, v, t5);
                        v = Load128(row, x + (6 * step));
                        low = AdvSimd.MultiplyWideningLowerAndAdd(low, v.GetLower(), t6.GetLower());
                        high = AdvSimd.MultiplyWideningUpperAndAdd(high, v, t6);
                        v = Load128(row, x + (7 * step));
                        low = AdvSimd.MultiplyWideningLowerAndAdd(low, v.GetLower(), t7.GetLower());
                        high = AdvSimd.MultiplyWideningUpperAndAdd(high, v, t7);
                    }

                    Vector128.Narrow(low >> shift, high >> shift).CopyTo(target[x..]);
                }
            }

            return;
        }

        // The taps by pairs
        var p01 = TapPair(filter[0], filter[1]);
        var p23 = TapPair(filter[2], filter[3]);
        var p45 = luma ? TapPair(filter[4], filter[5]) : default;
        var p67 = luma ? TapPair(filter[6], filter[7]) : default;
        var mask = Vector128.Create(0xFFFF);
        var wide = Avx2.IsSupported && width >= 16;
        var q01 = wide ? Vector256.Create(p01, p01) : default;
        var q23 = wide ? Vector256.Create(p23, p23) : default;
        var q45 = wide ? Vector256.Create(p45, p45) : default;
        var q67 = wide ? Vector256.Create(p67, p67) : default;
        var mask256 = Vector256.Create(0xFFFF);
        for (var r = 0; r < rows; r++)
        {
            var row = input[(r * inputStride)..];
            var target = output.Slice(r * outputStride, width);
            var x = 0;
            if (wide)
            {
                // The unpacking and the packing work in each 128-bit half: the low sums of a half are its samples 0 to 3, the
                // high sums its samples 4 to 7, and the packing puts them back in order
                for (; x <= width - 16; x += 16)
                {
                    var a = Load256(row, x);
                    var b = Load256(row, x + step);
                    var low = Avx2.MultiplyAddAdjacent(Avx2.UnpackLow(a, b), q01);
                    var high = Avx2.MultiplyAddAdjacent(Avx2.UnpackHigh(a, b), q01);
                    a = Load256(row, x + (2 * step));
                    b = Load256(row, x + (3 * step));
                    low += Avx2.MultiplyAddAdjacent(Avx2.UnpackLow(a, b), q23);
                    high += Avx2.MultiplyAddAdjacent(Avx2.UnpackHigh(a, b), q23);
                    if (luma)
                    {
                        a = Load256(row, x + (4 * step));
                        b = Load256(row, x + (5 * step));
                        low += Avx2.MultiplyAddAdjacent(Avx2.UnpackLow(a, b), q45);
                        high += Avx2.MultiplyAddAdjacent(Avx2.UnpackHigh(a, b), q45);
                        a = Load256(row, x + (6 * step));
                        b = Load256(row, x + (7 * step));
                        low += Avx2.MultiplyAddAdjacent(Avx2.UnpackLow(a, b), q67);
                        high += Avx2.MultiplyAddAdjacent(Avx2.UnpackHigh(a, b), q67);
                    }

                    // The low 16 bits of the sums, as the scalar conversion keeps them
                    Avx2.PackUnsignedSaturate((low >> shift) & mask256, (high >> shift) & mask256).AsInt16().CopyTo(target[x..]);
                }
            }

            for (; x < width; x += 8)
            {
                var a = Load128(row, x);
                var b = Load128(row, x + step);
                var low = Sse2.MultiplyAddAdjacent(Sse2.UnpackLow(a, b), p01);
                var high = Sse2.MultiplyAddAdjacent(Sse2.UnpackHigh(a, b), p01);
                a = Load128(row, x + (2 * step));
                b = Load128(row, x + (3 * step));
                low += Sse2.MultiplyAddAdjacent(Sse2.UnpackLow(a, b), p23);
                high += Sse2.MultiplyAddAdjacent(Sse2.UnpackHigh(a, b), p23);
                if (luma)
                {
                    a = Load128(row, x + (4 * step));
                    b = Load128(row, x + (5 * step));
                    low += Sse2.MultiplyAddAdjacent(Sse2.UnpackLow(a, b), p45);
                    high += Sse2.MultiplyAddAdjacent(Sse2.UnpackHigh(a, b), p45);
                    a = Load128(row, x + (6 * step));
                    b = Load128(row, x + (7 * step));
                    low += Sse2.MultiplyAddAdjacent(Sse2.UnpackLow(a, b), p67);
                    high += Sse2.MultiplyAddAdjacent(Sse2.UnpackHigh(a, b), p67);
                }

                Sse41.PackUnsignedSaturate((low >> shift) & mask, (high >> shift) & mask).AsInt16().CopyTo(target[x..]);
            }
        }
    }

    // The filter of 4 columns of rows of samples, as FilterRowWidening, the 4 samples of two rows in a vector: the sample x
    // of the output row r is the sum of the products of the taps by the samples ( r * inputStride ) + x + i * step of the
    // input. The last row of an odd number of rows is filtered twice
    private static void FilterQuads<T>(ReadOnlySpan<T> input, int inputStride, int step, ReadOnlySpan<sbyte> filter, int shift, Span<short> output, int outputStride, int rows)
        where T : unmanaged
    {
        var luma = filter.Length is 8;
        if (AdvSimd.Arm64.IsSupported)
        {
            var t0 = Vector128.Create((short)filter[0]);
            var t1 = Vector128.Create((short)filter[1]);
            var t2 = Vector128.Create((short)filter[2]);
            var t3 = Vector128.Create((short)filter[3]);
            var t4 = luma ? Vector128.Create((short)filter[4]) : default;
            var t5 = luma ? Vector128.Create((short)filter[5]) : default;
            var t6 = luma ? Vector128.Create((short)filter[6]) : default;
            var t7 = luma ? Vector128.Create((short)filter[7]) : default;
            for (var r = 0; r < rows; r += 2)
            {
                var first = r * inputStride;
                var second = Math.Min(r + 1, rows - 1) * inputStride;
                var v = LoadQuads(input, first, second);
                var low = AdvSimd.MultiplyWideningLower(v.GetLower(), t0.GetLower());
                var high = AdvSimd.MultiplyWideningUpper(v, t0);
                v = LoadQuads(input, first + step, second + step);
                low = AdvSimd.MultiplyWideningLowerAndAdd(low, v.GetLower(), t1.GetLower());
                high = AdvSimd.MultiplyWideningUpperAndAdd(high, v, t1);
                v = LoadQuads(input, first + (2 * step), second + (2 * step));
                low = AdvSimd.MultiplyWideningLowerAndAdd(low, v.GetLower(), t2.GetLower());
                high = AdvSimd.MultiplyWideningUpperAndAdd(high, v, t2);
                v = LoadQuads(input, first + (3 * step), second + (3 * step));
                low = AdvSimd.MultiplyWideningLowerAndAdd(low, v.GetLower(), t3.GetLower());
                high = AdvSimd.MultiplyWideningUpperAndAdd(high, v, t3);
                if (luma)
                {
                    v = LoadQuads(input, first + (4 * step), second + (4 * step));
                    low = AdvSimd.MultiplyWideningLowerAndAdd(low, v.GetLower(), t4.GetLower());
                    high = AdvSimd.MultiplyWideningUpperAndAdd(high, v, t4);
                    v = LoadQuads(input, first + (5 * step), second + (5 * step));
                    low = AdvSimd.MultiplyWideningLowerAndAdd(low, v.GetLower(), t5.GetLower());
                    high = AdvSimd.MultiplyWideningUpperAndAdd(high, v, t5);
                    v = LoadQuads(input, first + (6 * step), second + (6 * step));
                    low = AdvSimd.MultiplyWideningLowerAndAdd(low, v.GetLower(), t6.GetLower());
                    high = AdvSimd.MultiplyWideningUpperAndAdd(high, v, t6);
                    v = LoadQuads(input, first + (7 * step), second + (7 * step));
                    low = AdvSimd.MultiplyWideningLowerAndAdd(low, v.GetLower(), t7.GetLower());
                    high = AdvSimd.MultiplyWideningUpperAndAdd(high, v, t7);
                }

                StoreQuads(Vector128.Narrow(low >> shift, high >> shift), output, r * outputStride, r + 1 < rows ? (r + 1) * outputStride : -1);
            }

            return;
        }

        var p01 = TapPair(filter[0], filter[1]);
        var p23 = TapPair(filter[2], filter[3]);
        var p45 = luma ? TapPair(filter[4], filter[5]) : default;
        var p67 = luma ? TapPair(filter[6], filter[7]) : default;
        var mask = Vector128.Create(0xFFFF);
        for (var r = 0; r < rows; r += 2)
        {
            var first = r * inputStride;
            var second = Math.Min(r + 1, rows - 1) * inputStride;
            var a = LoadQuads(input, first, second);
            var b = LoadQuads(input, first + step, second + step);
            var low = Sse2.MultiplyAddAdjacent(Sse2.UnpackLow(a, b), p01);
            var high = Sse2.MultiplyAddAdjacent(Sse2.UnpackHigh(a, b), p01);
            a = LoadQuads(input, first + (2 * step), second + (2 * step));
            b = LoadQuads(input, first + (3 * step), second + (3 * step));
            low += Sse2.MultiplyAddAdjacent(Sse2.UnpackLow(a, b), p23);
            high += Sse2.MultiplyAddAdjacent(Sse2.UnpackHigh(a, b), p23);
            if (luma)
            {
                a = LoadQuads(input, first + (4 * step), second + (4 * step));
                b = LoadQuads(input, first + (5 * step), second + (5 * step));
                low += Sse2.MultiplyAddAdjacent(Sse2.UnpackLow(a, b), p45);
                high += Sse2.MultiplyAddAdjacent(Sse2.UnpackHigh(a, b), p45);
                a = LoadQuads(input, first + (6 * step), second + (6 * step));
                b = LoadQuads(input, first + (7 * step), second + (7 * step));
                low += Sse2.MultiplyAddAdjacent(Sse2.UnpackLow(a, b), p67);
                high += Sse2.MultiplyAddAdjacent(Sse2.UnpackHigh(a, b), p67);
            }

            StoreQuads(Sse41.PackUnsignedSaturate((low >> shift) & mask, (high >> shift) & mask).AsInt16(), output, r * outputStride, r + 1 < rows ? (r + 1) * outputStride : -1);
        }
    }

    // The 4 samples at first, then the 4 samples at second
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<short> LoadQuads<T>(ReadOnlySpan<T> input, int first, int second)
        where T : unmanaged
        => Vector128.Create(Vector64.Create(input.Slice(first, Vector64<T>.Count)), Vector64.Create(input.Slice(second, Vector64<T>.Count))).AsInt16();

    // The lanes 0 to 3 at first, the lanes 4 to 7 at second unless it is negative
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void StoreQuads(Vector128<short> values, Span<short> output, int first, int second)
    {
        values.GetLower().CopyTo(output.Slice(first, 4));
        if (second >= 0)
        {
            values.GetUpper().CopyTo(output.Slice(second, 4));
        }
    }

    // Two taps in each 32-bit lane: the low 16 bits multiply the first sample of a pair
    private static Vector128<short> TapPair(short first, short second) => Vector128.Create(first, second, first, second, first, second, first, second);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<short> Load128<T>(ReadOnlySpan<T> input, int index)
        where T : unmanaged
        => Vector128.Create(input.Slice(index, Vector128<T>.Count)).AsInt16();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<short> Load256<T>(ReadOnlySpan<T> input, int index)
        where T : unmanaged
        => Vector256.Create(input.Slice(index, Vector256<T>.Count)).AsInt16();

    // The interpolation of the columns from firstColumn of a block, from its window of reference samples, one sample at a
    // time (Equations 8-230 to 8-249 and 8-250 to 8-261)
    private static void Interpolate(ReadOnlySpan<ushort> source, int stride, int xFrac, int yFrac, int width, int height, int taps, ReadOnlySpan<sbyte> filters, int bitDepth, Span<short> destination, Span<short> intermediate, int firstColumn)
    {
        var shift1 = Math.Min(4, bitDepth - 8);
        var shift3 = 14 - bitDepth;
        if (xFrac is 0 && yFrac is 0)
        {
            for (var y = 0; y < height; y++)
            {
                var row = source.Slice(y * stride, width);
                var target = destination.Slice(y * width, width);
                for (var x = firstColumn; x < width; x++)
                {
                    target[x] = (short)(row[x] << shift3);
                }
            }

            return;
        }

        if (yFrac is 0)
        {
            var filter = filters.Slice(xFrac * taps, taps);
            for (var y = 0; y < height; y++)
            {
                var row = source.Slice(y * stride, width + taps - 1);
                var target = destination.Slice(y * width, width);
                for (var x = firstColumn; x < width; x++)
                {
                    var sum = 0;
                    for (var i = 0; i < taps; i++)
                    {
                        sum += filter[i] * row[x + i];
                    }

                    target[x] = (short)(sum >> shift1);
                }
            }

            return;
        }

        if (xFrac is 0)
        {
            var filter = filters.Slice(yFrac * taps, taps);
            for (var y = 0; y < height; y++)
            {
                var target = destination.Slice(y * width, width);
                for (var x = firstColumn; x < width; x++)
                {
                    var sum = 0;
                    for (var i = 0; i < taps; i++)
                    {
                        sum += filter[i] * source[((y + i) * stride) + x];
                    }

                    target[x] = (short)(sum >> shift1);
                }
            }

            return;
        }

        var horizontal = filters.Slice(xFrac * taps, taps);
        var vertical = filters.Slice(yFrac * taps, taps);
        var windowHeight = height + taps - 1;
        for (var y = 0; y < windowHeight; y++)
        {
            var row = source.Slice(y * stride, width + taps - 1);
            var target = intermediate.Slice(y * 64, width);
            for (var x = firstColumn; x < width; x++)
            {
                var sum = 0;
                for (var i = 0; i < taps; i++)
                {
                    sum += horizontal[i] * row[x + i];
                }

                target[x] = (short)(sum >> shift1);
            }
        }

        for (var y = 0; y < height; y++)
        {
            var target = destination.Slice(y * width, width);
            for (var x = firstColumn; x < width; x++)
            {
                var sum = 0;
                for (var i = 0; i < taps; i++)
                {
                    sum += vertical[i] * intermediate[((y + i) * 64) + x];
                }

                target[x] = (short)(sum >> 6);
            }
        }
    }

    /// <summary>The prediction of <see cref="Predict"/>, one sample at a time: the reference of the vector path.</summary>
    internal static void PredictScalar(Av1Plane reference, int xInt, int yInt, int xFrac, int yFrac, int width, int height, bool luma, int bitDepth, Span<short> destination, Span<short> intermediate, Span<ushort> window)
    {
        var taps = luma ? 8 : 4;
        var before = (taps >> 1) - 1;
        var filters = luma ? LumaFilter : ChromaFilter;
        var marginX = xFrac is not 0 ? taps - 1 : 0;
        var marginY = yFrac is not 0 ? taps - 1 : 0;
        var x0 = xInt - (xFrac is not 0 ? before : 0);
        var y0 = yInt - (yFrac is not 0 ? before : 0);
        var windowWidth = width + marginX;
        var windowHeight = height + marginY;

        // The reference samples of the block and of its filters: those of the plane, or a copy when some are outside it
        ReadOnlySpan<ushort> source;
        int stride;
        if (x0 >= 0 && y0 >= 0 && x0 + windowWidth <= reference.Width && y0 + windowHeight <= reference.Height)
        {
            stride = reference.Stride;
            source = reference.Samples[((y0 * stride) + x0)..];
        }
        else
        {
            // Equations 8-228 and 8-229: the coordinates are clipped to the reference picture
            var samples = reference.Samples;
            var maxX = reference.Width - 1;
            var maxY = reference.Height - 1;
            stride = windowWidth;
            for (var y = 0; y < windowHeight; y++)
            {
                var row = samples.Slice(Math.Clamp(y0 + y, 0, maxY) * reference.Stride, reference.Width);
                var target = window.Slice(y * windowWidth, windowWidth);
                for (var x = 0; x < windowWidth; x++)
                {
                    target[x] = row[Math.Clamp(x0 + x, 0, maxX)];
                }
            }

            source = window;
        }

        var shift1 = Math.Min(4, bitDepth - 8);
        var shift3 = 14 - bitDepth;
        if (xFrac is 0 && yFrac is 0)
        {
            for (var y = 0; y < height; y++)
            {
                var row = source.Slice(y * stride, width);
                var target = destination.Slice(y * width, width);
                for (var x = 0; x < width; x++)
                {
                    target[x] = (short)(row[x] << shift3);
                }
            }

            return;
        }

        if (yFrac is 0)
        {
            var filter = filters.Slice(xFrac * taps, taps);
            for (var y = 0; y < height; y++)
            {
                var row = source.Slice(y * stride, windowWidth);
                var target = destination.Slice(y * width, width);
                for (var x = 0; x < width; x++)
                {
                    var sum = 0;
                    for (var i = 0; i < taps; i++)
                    {
                        sum += filter[i] * row[x + i];
                    }

                    target[x] = (short)(sum >> shift1);
                }
            }

            return;
        }

        if (xFrac is 0)
        {
            var filter = filters.Slice(yFrac * taps, taps);
            for (var y = 0; y < height; y++)
            {
                var target = destination.Slice(y * width, width);
                for (var x = 0; x < width; x++)
                {
                    var sum = 0;
                    for (var i = 0; i < taps; i++)
                    {
                        sum += filter[i] * source[((y + i) * stride) + x];
                    }

                    target[x] = (short)(sum >> shift1);
                }
            }

            return;
        }

        // The rows that the vertical filter reads are the output of the horizontal filter
        var horizontal = filters.Slice(xFrac * taps, taps);
        var vertical = filters.Slice(yFrac * taps, taps);
        for (var y = 0; y < windowHeight; y++)
        {
            var row = source.Slice(y * stride, windowWidth);
            var target = intermediate.Slice(y * 64, width);
            for (var x = 0; x < width; x++)
            {
                var sum = 0;
                for (var i = 0; i < taps; i++)
                {
                    sum += horizontal[i] * row[x + i];
                }

                target[x] = (short)(sum >> shift1);
            }
        }

        for (var y = 0; y < height; y++)
        {
            var target = destination.Slice(y * width, width);
            for (var x = 0; x < width; x++)
            {
                var sum = 0;
                for (var i = 0; i < taps; i++)
                {
                    sum += vertical[i] * intermediate[((y + i) * 64) + x];
                }

                target[x] = (short)(sum >> 6);
            }
        }
    }

    /// <summary>
    /// The default weighted sample prediction process (clause 8.5.3.3.4.2) of a block predicted from one picture
    /// (Equation 8-262).
    /// </summary>
    public static void WeightDefault(ReadOnlySpan<short> prediction, Span<ushort> destination, int stride, int width, int height, int bitDepth, bool useVectors = true)
    {
        var shift1 = 14 - bitDepth;
        var offset1 = shift1 > 0 ? 1 << (shift1 - 1) : 0;
        var maxValue = (1 << bitDepth) - 1;
        var vectorWidth = useVectors && Vector128.IsHardwareAccelerated ? width & ~7 : 0;
        var offset = Vector128.Create(offset1);
        var max = Vector128.Create(maxValue);
        for (var y = 0; y < height; y++)
        {
            var source = prediction.Slice(y * width, width);
            var target = destination.Slice(y * stride, width);
            for (var x = 0; x < vectorWidth; x += 8)
            {
                var (low, high) = Vector128.Widen(Vector128.Create(source[x..]));
                Store(Clip((low + offset) >> shift1, max), Clip((high + offset) >> shift1, max), target[x..]);
            }

            for (var x = vectorWidth; x < width; x++)
            {
                target[x] = (ushort)Math.Clamp((source[x] + offset1) >> shift1, 0, maxValue);
            }
        }
    }

    /// <summary>The default weighted sample prediction process of a block predicted from two pictures (Equation 8-264).</summary>
    public static void WeightDefault(ReadOnlySpan<short> prediction0, ReadOnlySpan<short> prediction1, Span<ushort> destination, int stride, int width, int height, int bitDepth, bool useVectors = true)
    {
        var shift2 = 15 - bitDepth;
        var offset2 = 1 << (shift2 - 1);
        var maxValue = (1 << bitDepth) - 1;
        var vectorWidth = useVectors && Vector128.IsHardwareAccelerated ? width & ~7 : 0;
        var offset = Vector128.Create(offset2);
        var max = Vector128.Create(maxValue);
        for (var y = 0; y < height; y++)
        {
            var source0 = prediction0.Slice(y * width, width);
            var source1 = prediction1.Slice(y * width, width);
            var target = destination.Slice(y * stride, width);
            for (var x = 0; x < vectorWidth; x += 8)
            {
                var (low0, high0) = Vector128.Widen(Vector128.Create(source0[x..]));
                var (low1, high1) = Vector128.Widen(Vector128.Create(source1[x..]));
                Store(Clip((low0 + low1 + offset) >> shift2, max), Clip((high0 + high1 + offset) >> shift2, max), target[x..]);
            }

            for (var x = vectorWidth; x < width; x++)
            {
                target[x] = (ushort)Math.Clamp((source0[x] + source1[x] + offset2) >> shift2, 0, maxValue);
            }
        }
    }

    /// <summary>
    /// The explicit weighted sample prediction process (clause 8.5.3.3.4.3) of a block predicted from one picture
    /// (Equation 8-265): <paramref name="weight"/> is <c>w0</c> or <c>w1</c>, <paramref name="offset"/> is <c>o0</c> or
    /// <c>o1</c> at the bit depth of the samples, and <paramref name="log2Wd"/> is the base 2 logarithm of the denominator
    /// of the weights, plus <c>shift1</c>.
    /// </summary>
    public static void WeightExplicit(ReadOnlySpan<short> prediction, Span<ushort> destination, int stride, int width, int height, int bitDepth, int weight, int offset, int log2Wd, bool useVectors = true)
    {
        var maxValue = (1 << bitDepth) - 1;
        var rounding = log2Wd >= 1 ? 1 << (log2Wd - 1) : 0;
        var vectorWidth = useVectors && Vector128.IsHardwareAccelerated ? width & ~7 : 0;
        var roundingVector = Vector128.Create(rounding);
        var offsetVector = Vector128.Create(offset);
        var max = Vector128.Create(maxValue);
        for (var y = 0; y < height; y++)
        {
            var source = prediction.Slice(y * width, width);
            var target = destination.Slice(y * stride, width);
            for (var x = 0; x < vectorWidth; x += 8)
            {
                var (low, high) = Vector128.Widen(Vector128.Create(source[x..]));
                Store(Clip((((low * weight) + roundingVector) >> log2Wd) + offsetVector, max), Clip((((high * weight) + roundingVector) >> log2Wd) + offsetVector, max), target[x..]);
            }

            for (var x = vectorWidth; x < width; x++)
            {
                target[x] = (ushort)Math.Clamp((((source[x] * weight) + rounding) >> log2Wd) + offset, 0, maxValue);
            }
        }
    }

    /// <summary>The explicit weighted sample prediction process of a block predicted from two pictures (Equation 8-266).</summary>
    public static void WeightExplicit(ReadOnlySpan<short> prediction0, ReadOnlySpan<short> prediction1, Span<ushort> destination, int stride, int width, int height, int bitDepth, int weight0, int weight1, int offset0, int offset1, int log2Wd, bool useVectors = true)
    {
        var maxValue = (1 << bitDepth) - 1;
        var rounding = (offset0 + offset1 + 1) << log2Wd;
        var vectorWidth = useVectors && Vector128.IsHardwareAccelerated ? width & ~7 : 0;
        var roundingVector = Vector128.Create(rounding);
        var max = Vector128.Create(maxValue);
        for (var y = 0; y < height; y++)
        {
            var source0 = prediction0.Slice(y * width, width);
            var source1 = prediction1.Slice(y * width, width);
            var target = destination.Slice(y * stride, width);
            for (var x = 0; x < vectorWidth; x += 8)
            {
                var (low0, high0) = Vector128.Widen(Vector128.Create(source0[x..]));
                var (low1, high1) = Vector128.Widen(Vector128.Create(source1[x..]));
                Store(
                    Clip(((low0 * weight0) + (low1 * weight1) + roundingVector) >> (log2Wd + 1), max),
                    Clip(((high0 * weight0) + (high1 * weight1) + roundingVector) >> (log2Wd + 1), max),
                    target[x..]);
            }

            for (var x = vectorWidth; x < width; x++)
            {
                target[x] = (ushort)Math.Clamp(((source0[x] * weight0) + (source1[x] * weight1) + rounding) >> (log2Wd + 1), 0, maxValue);
            }
        }
    }

    // The samples of 8 lanes clipped to the range of the bit depth: Math.Clamp( value, 0, maxValue )
    private static Vector128<int> Clip(Vector128<int> value, Vector128<int> max) => Vector128.Min(Vector128.Max(value, Vector128<int>.Zero), max);

    private static void Store(Vector128<int> low, Vector128<int> high, Span<ushort> destination) => Vector128.Narrow(low.AsUInt32(), high.AsUInt32()).CopyTo(destination);
}
