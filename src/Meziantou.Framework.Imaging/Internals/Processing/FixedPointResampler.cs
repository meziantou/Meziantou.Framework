using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The integer resampler of 8-bit pixels without alpha, or whose pixels read are all opaque, filtered as stored (encoded
/// samples): <see cref="ImageProcessingExtensions.Resize"/> with a filter, gathering each output row from a ring of
/// horizontally resampled source rows like <see cref="Resampler"/>.
/// </summary>
/// <remarks>
/// <para>
/// The weights are those of <see cref="ResampleWeights"/> in fixed point (<see cref="FixedPointWeights"/>: 14 fractional
/// bits, each window adding up to exactly 2^14). A horizontal sum <c>H = w0 x0 + w1 x1 + ...</c> of 8-bit samples is
/// computed in 32 bits and kept in 16 bits with <c>P</c> fractional bits: <c>Hq = (H + 2^(13 - P)) &gt;&gt; (14 - P)</c>.
/// The vertical sum <c>V = v0 Hq0 + v1 Hq1 + ...</c> is computed in 32 bits and gives the sample
/// <c>clamp((V + 2^(13 + P)) &gt;&gt; (14 + P), 0, 255)</c>: rounded to nearest, ties upward.
/// </para>
/// <para>
/// <b>Headroom.</b> <c>P</c> is the largest value from 7 down to 0 (<see cref="GetPrecision"/>) for which, for every window
/// of the plan, <c>255 S+</c> and <c>255 S-</c> (the sums of its positive and negative weights) give values of
/// <c>Hq</c> within the 16-bit range, and <c>max|Hq| * sum|v| + 2^(13 + P)</c> fits 32 bits. Every horizontal sum lies
/// between <c>255 S-</c> and <c>255 S+</c> whatever the samples, so no sum can overflow: the bounds are checked for the
/// weights of the plan, not assumed. With weights that add up to 1, <c>S+</c> is 1 for bilinear (P = 7: 255 * 128 =
/// 32,640), and below 2 with the negative lobes of Catmull-Rom and Lanczos3 (P = 6). A plan for which no <c>P</c> fits
/// keeps the double-precision path.
/// </para>
/// <para>
/// The sums are integers, so their order does not matter: the vector loops (AdvSimd <c>smlal</c>, SSE2 and AVX2
/// <c>pmaddwd</c>) give the bits of the scalar loops (<c>VectorizedKernelTests</c>), whatever the hardware and the
/// worker count. The result is the exact rounding of the double-precision path except for about 0.2% of the samples,
/// which differ by one unit (the fixed-point weights and the 16-bit intermediate rows); none differs by more.
/// Pixels with translucent alpha are not filtered here: their colors are premultiplied, and dividing integer sums by a
/// small alpha amplifies their rounding beyond one unit.
/// </para>
/// <para>
/// An opaque 4-sample pixel is filtered with its alpha, which stays exactly at its maximum since every window adds up to
/// exactly 2^14 in both directions; a 3-sample pixel is filtered as 4 lanes, the fourth one zero.
/// </para>
/// </remarks>
internal static class FixedPointResampler
{
    /// <summary>Gets the number of taps the 1-lane horizontal windows are padded to a multiple of (one 128-bit vector of 16-bit samples).</summary>
    internal const int GrayTapMultiple = 8;

    /// <summary>Gets the number of taps the 4-lane horizontal windows are padded to a multiple of (pairs for <c>pmaddwd</c>).</summary>
    internal const int ColorTapMultiple = 2;

    /// <summary>Gets the number of 16-bit lanes of a working pixel: 1 for gray, 4 otherwise.</summary>
    internal static int GetLanes(int channels) => channels is 1 ? 1 : 4;

    /// <summary>Gets the number of fractional bits of the intermediate rows, or -1 when no precision fits (see the remarks of the class).</summary>
    internal static int GetPrecision(FixedPointWeights horizontal, FixedPointWeights vertical)
    {
        for (var precision = 7; precision >= 0; precision--)
        {
            var shift = FixedPointWeights.FractionBits - precision;
            var high = ((byte.MaxValue * horizontal.MaxPositiveSum) + (1L << (shift - 1))) >> shift;
            var low = ((byte.MaxValue * horizontal.MinNegativeSum) + (1L << (shift - 1))) >> shift;
            if (high > short.MaxValue || low < short.MinValue)
                continue;

            var largest = Math.Max(high, -low);
            if ((vertical.MaxAbsoluteSum * largest) + (1L << (FixedPointWeights.FractionBits + precision - 1)) <= int.MaxValue)
                return precision;
        }

        return -1;
    }

    /// <summary>Output rows <paramref name="start"/> to <paramref name="end"/> - 1, gathering each from a ring of horizontally resampled source rows.</summary>
    public static void Gather(PixelStorage source, PixelStorage destination, ResizePlan plan, ResizeScratch scratch, int start, int end, CancellationToken cancellationToken)
    {
        var x = plan.XFixed!;
        var y = plan.YFixed!;
        var channels = plan.Channels;
        var lanes = GetLanes(channels);
        var precision = plan.FixedPrecision;
        var horizontalShift = FixedPointWeights.FractionBits - precision;
        var verticalShift = FixedPointWeights.FractionBits + precision;
        var outputWidth = plan.Geometry.OutputSize.Width;
        var rowLength = outputWidth * lanes;
        var ringSize = scratch.RowCount;

        // The double rows of the scratch hold the 16-bit rows: each slot keeps its position, the extra row holds the 8-bit
        // 4-lane output of 3-sample pixels before they are packed
        var rows = unsafe(MemoryMarshal.AsBytes(scratch.Rows));
        var stride = scratch.RowLength * sizeof(double) / sizeof(short);
        var ring = unsafe(MemoryMarshal.Cast<byte, short>(rows[(scratch.RowLength * sizeof(double))..]));
        var packed = channels is 3 ? rows[..(outputWidth * 4)] : default;
        var working = unsafe(MemoryMarshal.Cast<double, short>(scratch.SourceRow))[..(x.SourceLength * lanes)];
        var starts = y.Starts;
        var weights = y.AllWeights;
        var offset = 0;
        for (var dy = 0; dy < start; dy++)
        {
            offset += GetCount(plan, dy);
        }

        var next = starts[start];
        for (var dy = start; dy < end; dy++)
        {
            if (dy % ProcessingKernels.RowsPerCancellationCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var first = starts[dy];
            var count = GetCount(plan, dy);
            var last = first + count - 1;
            while (next <= last)
            {
                var samples = source.GetRowSpanCore(next).Slice(x.First * channels, (plan.XWeights!.Last - x.First + 1) * channels);
                LoadRow(samples, channels, working);
                ResampleHorizontal(working, ring.Slice(next % ringSize * stride, rowLength), x, lanes, horizontalShift);
                next++;
            }

            var output = destination.GetRowSpanCore(dy);
            ResampleVertical(ring, stride, first % ringSize, ringSize, weights.Slice(offset, count), channels is 3 ? packed : output[..rowLength], verticalShift);
            if (channels is 3)
            {
                PackRgb(packed, output[..(outputWidth * 3)]);
            }

            offset += count;
        }
    }

    private static int GetCount(ResizePlan plan, int index) => plan.YWeights!.GetCount(index);

    /// <summary>Converts 8-bit samples to 16-bit working lanes: 1 per gray sample, 4 per pixel otherwise (3-sample pixels get a zero fourth lane).</summary>
    internal static void LoadRow(ReadOnlySpan<byte> samples, int channels, Span<short> working)
    {
        var i = 0;
        if (!Resampler.TestForceScalar && Vector128.IsHardwareAccelerated)
        {
            if (channels is 3)
            {
                if (Ssse3.IsSupported || AdvSimd.Arm64.IsSupported)
                {
                    // 4 pixels at a time from 16 loaded bytes (the last 4 belong to the next pixels: at least 2 more must follow)
                    ref var input = ref unsafe(MemoryMarshal.GetReference(samples));
                    ref var output = ref unsafe(Unsafe.As<short, byte>(ref MemoryMarshal.GetReference(working)));
                    var indices = Vector128.Create((byte)0, 1, 2, 0xFF, 3, 4, 5, 0xFF, 6, 7, 8, 0xFF, 9, 10, 11, 0xFF);
                    for (; i + 16 <= samples.Length; i += 12)
                    {
                        var expanded = Vector128.Shuffle(unsafe(Vector128.LoadUnsafe(ref input, (nuint)i)), indices);
                        var (low, high) = Vector128.Widen(expanded);
                        unsafe { low.StoreUnsafe(ref Unsafe.As<byte, ushort>(ref output), (nuint)(i / 3 * 4)); }
                        unsafe { high.StoreUnsafe(ref Unsafe.As<byte, ushort>(ref output), (nuint)((i / 3 * 4) + 8)); }
                    }
                }
            }
            else
            {
                ref var input = ref unsafe(MemoryMarshal.GetReference(samples));
                ref var output = ref unsafe(Unsafe.As<short, ushort>(ref MemoryMarshal.GetReference(working)));
                for (; i + 16 <= samples.Length; i += 16)
                {
                    var (low, high) = Vector128.Widen(unsafe(Vector128.LoadUnsafe(ref input, (nuint)i)));
                    unsafe { low.StoreUnsafe(ref output, (nuint)i); }
                    unsafe { high.StoreUnsafe(ref output, (nuint)(i + 8)); }
                }
            }
        }

        if (channels is 3)
        {
            for (; i < samples.Length; i += 3)
            {
                var o = i / 3 * 4;
                working[o] = samples[i];
                working[o + 1] = samples[i + 1];
                working[o + 2] = samples[i + 2];
                working[o + 3] = 0;
            }
        }
        else
        {
            for (; i < samples.Length; i++)
            {
                working[i] = samples[i];
            }
        }
    }

    /// <summary>Packs 4-lane 8-bit pixels into 3-sample pixels.</summary>
    internal static void PackRgb(ReadOnlySpan<byte> pixels, Span<byte> output)
    {
        Debug.Assert(pixels.Length / 4 * 3 == output.Length);
        var o = 0;
        var i = 0;
        if (!Resampler.TestForceScalar && (Ssse3.IsSupported || AdvSimd.Arm64.IsSupported))
        {
            // 4 pixels at a time, stored as 16 bytes of which the last 4 are overwritten by the next pixels
            ref var input = ref unsafe(MemoryMarshal.GetReference(pixels));
            ref var target = ref unsafe(MemoryMarshal.GetReference(output));
            var indices = Vector128.Create((byte)0, 1, 2, 4, 5, 6, 8, 9, 10, 12, 13, 14, 0xFF, 0xFF, 0xFF, 0xFF);
            for (; o + 16 <= output.Length; i += 16, o += 12)
            {
                unsafe { Vector128.Shuffle(Vector128.LoadUnsafe(ref input, (nuint)i), indices).StoreUnsafe(ref target, (nuint)o); }
            }
        }

        for (; o < output.Length; i += 4, o += 3)
        {
            output[o] = pixels[i];
            output[o + 1] = pixels[i + 1];
            output[o + 2] = pixels[i + 2];
        }
    }

    /// <summary>
    /// Resamples one working row horizontally into 16-bit lanes with <c>14 - P</c> = <paramref name="shift"/>:
    /// <c>(w0 x0 + w1 x1 + ... + 2^(shift - 1)) &gt;&gt; shift</c> per lane.
    /// </summary>
    internal static void ResampleHorizontal(ReadOnlySpan<short> working, Span<short> output, FixedPointWeights weights, int lanes, int shift)
    {
        if (Resampler.TestForceScalar)
        {
            ResampleHorizontalScalar(working, output, weights, lanes, shift);
        }
        else if (lanes is 4)
        {
            if (AdvSimd.IsSupported)
            {
                ResampleHorizontal4AdvSimd(working, output, weights, shift);
            }
            else if (Sse2.IsSupported)
            {
                ResampleHorizontal4Sse2(working, output, weights, shift);
            }
            else
            {
                ResampleHorizontalScalar(working, output, weights, lanes, shift);
            }
        }
        else if (AdvSimd.IsSupported || Sse2.IsSupported)
        {
            ResampleHorizontal1Vector128(working, output, weights, shift);
        }
        else
        {
            ResampleHorizontalScalar(working, output, weights, lanes, shift);
        }
    }

    /// <summary>The scalar reference of <see cref="ResampleHorizontal"/>.</summary>
    internal static void ResampleHorizontalScalar(ReadOnlySpan<short> working, Span<short> output, FixedPointWeights weights, int lanes, int shift)
    {
        var starts = weights.Starts;
        var round = 1 << (shift - 1);
        for (var dx = 0; dx < weights.OutputLength; dx++)
        {
            var taps = weights.GetPaddedWeights(dx);
            var input = working.Slice(starts[dx] * lanes, taps.Length * lanes);
            for (var lane = 0; lane < lanes; lane++)
            {
                var sum = 0;
                for (var k = 0; k < taps.Length; k++)
                {
                    sum += taps[k] * input[(k * lanes) + lane];
                }

                output[(dx * lanes) + lane] = (short)((sum + round) >> shift);
            }
        }
    }

    // One pixel per iteration: the 4 lanes of each tap multiplied by its weight and added to 32-bit lanes (smlal)
    private static void ResampleHorizontal4AdvSimd(ReadOnlySpan<short> working, Span<short> output, FixedPointWeights weights, int shift)
    {
        var starts = weights.Starts;
        var taps = weights.Taps;
        var round = Vector128.Create(1 << (shift - 1));
        ref var input = ref unsafe(MemoryMarshal.GetReference(working));
        ref var target = ref unsafe(MemoryMarshal.GetReference(output));
        ref var weight = ref unsafe(MemoryMarshal.GetReference(weights.AllWeights));
        Debug.Assert(output.Length >= weights.OutputLength * 4 && working.Length >= weights.SourceLength * 4);
        for (var dx = 0; dx < weights.OutputLength; dx++)
        {
            ref var pixel = ref unsafe(Unsafe.Add(ref input, starts[dx] * 4));
            ref var w = ref unsafe(Unsafe.Add(ref weight, dx * taps));
            var even = Vector128<int>.Zero;
            var odd = Vector128<int>.Zero;
            for (var k = 0; k < taps; k += 2)
            {
                even = AdvSimd.MultiplyWideningLowerAndAdd(even, unsafe(Vector64.LoadUnsafe(ref pixel, (nuint)(4 * k))), Vector64.Create(unsafe(Unsafe.Add(ref w, k))));
                odd = AdvSimd.MultiplyWideningLowerAndAdd(odd, unsafe(Vector64.LoadUnsafe(ref pixel, (nuint)((4 * k) + 4))), Vector64.Create(unsafe(Unsafe.Add(ref w, k + 1))));
            }

            var sum = Vector128.ShiftRightArithmetic(even + odd + round, shift);
            unsafe { AdvSimd.ExtractNarrowingLower(sum).StoreUnsafe(ref target, (nuint)(4 * dx)); }
        }
    }

    // One pixel per iteration: two taps (8 lanes) interleaved lane by lane, multiplied by the pair of weights and added
    // pairwise (pmaddwd)
    private static void ResampleHorizontal4Sse2(ReadOnlySpan<short> working, Span<short> output, FixedPointWeights weights, int shift)
    {
        var starts = weights.Starts;
        var taps = weights.Taps;
        var round = Vector128.Create(1 << (shift - 1));
        ref var input = ref unsafe(MemoryMarshal.GetReference(working));
        ref var target = ref unsafe(MemoryMarshal.GetReference(output));
        ref var weight = ref unsafe(MemoryMarshal.GetReference(weights.AllWeights));
        Debug.Assert(output.Length >= weights.OutputLength * 4 && working.Length >= weights.SourceLength * 4);
        for (var dx = 0; dx < weights.OutputLength; dx++)
        {
            ref var pixel = ref unsafe(Unsafe.Add(ref input, starts[dx] * 4));
            ref var w = ref unsafe(Unsafe.Add(ref weight, dx * taps));
            var sum = Vector128<int>.Zero;
            for (var k = 0; k < taps; k += 2)
            {
                var pair = unsafe(Vector128.LoadUnsafe(ref pixel, (nuint)(4 * k)));
                var interleaved = Sse2.UnpackLow(pair, Sse2.ShiftRightLogical128BitLane(pair, 8));
                var w2 = Vector128.Create(unsafe(Unsafe.ReadUnaligned<int>(ref Unsafe.As<short, byte>(ref Unsafe.Add(ref w, k))))).AsInt16();
                sum += Sse2.MultiplyAddAdjacent(interleaved, w2);
            }

            sum = Vector128.ShiftRightArithmetic(sum + round, shift);
            var narrowed = Vector128.Narrow(sum, sum);
            unsafe { Unsafe.WriteUnaligned(ref Unsafe.As<short, byte>(ref Unsafe.Add(ref target, 4 * dx)), narrowed.AsInt64().ToScalar()); }
        }
    }

    // One gray pixel per iteration: 8 taps at a time, multiplied and added to 32-bit lanes, then summed
    private static void ResampleHorizontal1Vector128(ReadOnlySpan<short> working, Span<short> output, FixedPointWeights weights, int shift)
    {
        var starts = weights.Starts;
        var taps = weights.Taps;
        var round = 1 << (shift - 1);
        ref var input = ref unsafe(MemoryMarshal.GetReference(working));
        ref var weight = ref unsafe(MemoryMarshal.GetReference(weights.AllWeights));
        Debug.Assert(output.Length >= weights.OutputLength && working.Length >= weights.SourceLength && taps % 8 == 0);
        for (var dx = 0; dx < weights.OutputLength; dx++)
        {
            ref var pixel = ref unsafe(Unsafe.Add(ref input, starts[dx]));
            ref var w = ref unsafe(Unsafe.Add(ref weight, dx * taps));
            var sum = Vector128<int>.Zero;
            for (var k = 0; k < taps; k += 8)
            {
                var samples = unsafe(Vector128.LoadUnsafe(ref pixel, (nuint)k));
                var tapWeights = unsafe(Vector128.LoadUnsafe(ref w, (nuint)k));
                if (AdvSimd.IsSupported)
                {
                    sum = AdvSimd.MultiplyWideningLowerAndAdd(sum, samples.GetLower(), tapWeights.GetLower());
                    sum = AdvSimd.MultiplyWideningUpperAndAdd(sum, samples, tapWeights);
                }
                else
                {
                    sum += Sse2.MultiplyAddAdjacent(samples, tapWeights);
                }
            }

            output[dx] = (short)((Vector128.Sum(sum) + round) >> shift);
        }
    }

    /// <summary>
    /// Sets the 8-bit samples of <paramref name="output"/> to <c>clamp((v0 r0 + v1 r1 + ... + 2^(shift - 1)) &gt;&gt; shift, 0, 255)</c>,
    /// with <c>r_k</c> the ring row <c>(firstSlot + k) % ringSize</c> (rows of <paramref name="stride"/> lanes).
    /// </summary>
    internal static void ResampleVertical(ReadOnlySpan<short> ring, int stride, int firstSlot, int ringSize, ReadOnlySpan<short> weights, Span<byte> output, int shift)
    {
        var i = 0;
        if (!Resampler.TestForceScalar)
        {
            if (AdvSimd.IsSupported)
            {
                i = ResampleVerticalAdvSimd(ring, stride, firstSlot, ringSize, weights, output, shift);
            }
            else if (Vector256.IsHardwareAccelerated && Avx2.IsSupported)
            {
                i = ResampleVerticalAvx2(ring, stride, firstSlot, ringSize, weights, output, shift);
            }
            else if (Sse2.IsSupported)
            {
                i = ResampleVerticalSse2(ring, stride, firstSlot, ringSize, weights, output, shift);
            }
        }

        ResampleVerticalScalar(ring, stride, firstSlot, ringSize, weights, output, shift, i);
    }

    /// <summary>The scalar reference of <see cref="ResampleVertical"/>, from sample <paramref name="start"/>.</summary>
    internal static void ResampleVerticalScalar(ReadOnlySpan<short> ring, int stride, int firstSlot, int ringSize, ReadOnlySpan<short> weights, Span<byte> output, int shift, int start = 0)
    {
        var round = 1 << (shift - 1);
        for (var i = start; i < output.Length; i++)
        {
            var sum = 0;
            var slot = firstSlot;
            for (var k = 0; k < weights.Length; k++)
            {
                sum += weights[k] * ring[(slot * stride) + i];
                slot = slot + 1 == ringSize ? 0 : slot + 1;
            }

            var value = (sum + round) >> shift;
            output[i] = (byte)(value < 0 ? 0 : value > byte.MaxValue ? byte.MaxValue : value);
        }
    }

    // 16 samples per iteration in four 32-bit accumulators (smlal, smlal2), narrowed with saturation
    private static int ResampleVerticalAdvSimd(ReadOnlySpan<short> ring, int stride, int firstSlot, int ringSize, ReadOnlySpan<short> weights, Span<byte> output, int shift)
    {
        ref var rows = ref unsafe(MemoryMarshal.GetReference(ring));
        ref var target = ref unsafe(MemoryMarshal.GetReference(output));
        var round = Vector128.Create(1 << (shift - 1));
        var i = 0;
        for (; i + 16 <= output.Length; i += 16)
        {
            var a = Vector128<int>.Zero;
            var b = Vector128<int>.Zero;
            var c = Vector128<int>.Zero;
            var d = Vector128<int>.Zero;
            var slot = firstSlot;
            for (var k = 0; k < weights.Length; k++)
            {
                ref var row = ref unsafe(Unsafe.Add(ref rows, (slot * stride) + i));
                var low = unsafe(Vector128.LoadUnsafe(ref row));
                var high = unsafe(Vector128.LoadUnsafe(ref row, 8));
                var w = Vector128.Create(weights[k]);
                a = AdvSimd.MultiplyWideningLowerAndAdd(a, low.GetLower(), w.GetLower());
                b = AdvSimd.MultiplyWideningUpperAndAdd(b, low, w);
                c = AdvSimd.MultiplyWideningLowerAndAdd(c, high.GetLower(), w.GetLower());
                d = AdvSimd.MultiplyWideningUpperAndAdd(d, high, w);
                slot = slot + 1 == ringSize ? 0 : slot + 1;
            }

            var first = AdvSimd.ExtractNarrowingSaturateUnsignedUpper(AdvSimd.ExtractNarrowingSaturateUnsignedLower(Vector128.ShiftRightArithmetic(a + round, shift)), Vector128.ShiftRightArithmetic(b + round, shift));
            var second = AdvSimd.ExtractNarrowingSaturateUnsignedUpper(AdvSimd.ExtractNarrowingSaturateUnsignedLower(Vector128.ShiftRightArithmetic(c + round, shift)), Vector128.ShiftRightArithmetic(d + round, shift));
            var bytes = AdvSimd.ExtractNarrowingSaturateUpper(AdvSimd.ExtractNarrowingSaturateLower(first), second);
            unsafe { bytes.StoreUnsafe(ref target, (nuint)i); }
        }

        return i;
    }

    // 16 samples per iteration: the rows of two taps interleaved lane by lane (within each 128-bit half), multiplied by the
    // pair of weights and added pairwise (pmaddwd); packing back with saturation restores the order of each half
    private static int ResampleVerticalAvx2(ReadOnlySpan<short> ring, int stride, int firstSlot, int ringSize, ReadOnlySpan<short> weights, Span<byte> output, int shift)
    {
        ref var rows = ref unsafe(MemoryMarshal.GetReference(ring));
        ref var target = ref unsafe(MemoryMarshal.GetReference(output));
        var round = Vector256.Create(1 << (shift - 1));
        var i = 0;
        for (; i + 16 <= output.Length; i += 16)
        {
            var low = Vector256<int>.Zero;
            var high = Vector256<int>.Zero;
            var slot = firstSlot;
            for (var k = 0; k < weights.Length; k += 2)
            {
                var a = unsafe(Vector256.LoadUnsafe(ref Unsafe.Add(ref rows, (slot * stride) + i)));
                slot = slot + 1 == ringSize ? 0 : slot + 1;
                var b = Vector256<short>.Zero;
                var pair = (int)(ushort)weights[k];
                if (k + 1 < weights.Length)
                {
                    b = unsafe(Vector256.LoadUnsafe(ref Unsafe.Add(ref rows, (slot * stride) + i)));
                    slot = slot + 1 == ringSize ? 0 : slot + 1;
                    pair |= weights[k + 1] << 16;
                }

                var w = Vector256.Create(pair).AsInt16();
                low += Avx2.MultiplyAddAdjacent(Avx2.UnpackLow(a, b), w);
                high += Avx2.MultiplyAddAdjacent(Avx2.UnpackHigh(a, b), w);
            }

            var words = Avx2.PackSignedSaturate(Vector256.ShiftRightArithmetic(low + round, shift), Vector256.ShiftRightArithmetic(high + round, shift));
            var bytes = Avx2.PackUnsignedSaturate(words, words);
            unsafe { Avx2.Permute4x64(bytes.AsInt64(), 0b_00_00_10_00).GetLower().AsByte().StoreUnsafe(ref target, (nuint)i); }
        }

        return i;
    }

    // 8 samples per iteration, as the AVX2 loop on one 128-bit half
    private static int ResampleVerticalSse2(ReadOnlySpan<short> ring, int stride, int firstSlot, int ringSize, ReadOnlySpan<short> weights, Span<byte> output, int shift)
    {
        ref var rows = ref unsafe(MemoryMarshal.GetReference(ring));
        ref var target = ref unsafe(MemoryMarshal.GetReference(output));
        var round = Vector128.Create(1 << (shift - 1));
        var i = 0;
        for (; i + 8 <= output.Length; i += 8)
        {
            var low = Vector128<int>.Zero;
            var high = Vector128<int>.Zero;
            var slot = firstSlot;
            for (var k = 0; k < weights.Length; k += 2)
            {
                var a = unsafe(Vector128.LoadUnsafe(ref Unsafe.Add(ref rows, (slot * stride) + i)));
                slot = slot + 1 == ringSize ? 0 : slot + 1;
                var b = Vector128<short>.Zero;
                var pair = (int)(ushort)weights[k];
                if (k + 1 < weights.Length)
                {
                    b = unsafe(Vector128.LoadUnsafe(ref Unsafe.Add(ref rows, (slot * stride) + i)));
                    slot = slot + 1 == ringSize ? 0 : slot + 1;
                    pair |= weights[k + 1] << 16;
                }

                var w = Vector128.Create(pair).AsInt16();
                low += Sse2.MultiplyAddAdjacent(Sse2.UnpackLow(a, b), w);
                high += Sse2.MultiplyAddAdjacent(Sse2.UnpackHigh(a, b), w);
            }

            var words = Sse2.PackSignedSaturate(Vector128.ShiftRightArithmetic(low + round, shift), Vector128.ShiftRightArithmetic(high + round, shift));
            unsafe { Unsafe.WriteUnaligned(ref Unsafe.Add(ref target, i), Sse2.PackUnsignedSaturate(words, words).AsInt64().ToScalar()); }
        }

        return i;
    }
}
