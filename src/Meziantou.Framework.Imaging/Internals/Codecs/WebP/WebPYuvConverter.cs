using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Converts reconstructed VP8 Y'CbCr 4:2:0 planes to RGB rows. The
/// WebP container specification recommends BT.601 without fixing the arithmetic, so this is the library's documented
/// contract, measured against libwebp and FFmpeg by the conformance and interop tests:
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>
/// Chroma is upsampled bilinearly with centered (MPEG-1/JPEG) siting: a luma pixel takes 9/16 of its chroma sample, 3/16 of
/// the horizontal and of the vertical neighbor on its side, and 1/16 of the diagonal neighbor,
/// <c>(9 a + 3 b + 3 c + d + 8) &gt;&gt; 4</c>; samples past the last chroma row or column repeat the edge.
/// </description></item>
/// <item><description>
/// The BT.601 limited-range matrix in 16-bit fixed point, rounded to nearest (ties up) and clamped:
/// <c>R = (76309 (Y - 16) + 104597 (Cr - 128) + 32768) &gt;&gt; 16</c>,
/// <c>G = (76309 (Y - 16) - 25675 (Cb - 128) - 53279 (Cr - 128) + 32768) &gt;&gt; 16</c>,
/// <c>B = (76309 (Y - 16) + 132201 (Cb - 128) + 32768) &gt;&gt; 16</c>
/// (the coefficients 255/219, 1.402 * 255/224, 0.344136 * 255/224, 0.714136 * 255/224 and 1.772 * 255/224, times 65,536).
/// </description></item>
/// </list>
/// </remarks>
internal static class WebPYuvConverter
{
    public const int LumaScale = 76309;
    public const int RedFromCr = 104597;
    public const int GreenFromCb = 25675;
    public const int GreenFromCr = 53279;
    public const int BlueFromCb = 132201;

    private static readonly int[] LumaTerm = CreateTable(static v => (LumaScale * (v - 16)) + 32768);
    private static readonly int[] RedCrTerm = CreateTable(static v => RedFromCr * (v - 128));
    private static readonly int[] GreenCbTerm = CreateTable(static v => GreenFromCb * (v - 128));
    private static readonly int[] GreenCrTerm = CreateTable(static v => GreenFromCr * (v - 128));
    private static readonly int[] BlueCbTerm = CreateTable(static v => BlueFromCb * (v - 128));

    /// <summary>Converts row <paramref name="y"/> of the planes to RGBA (alpha from <paramref name="alpha"/>, or opaque) or RGB pixels.</summary>
    /// <param name="planes">The reconstructed planes.</param>
    /// <param name="lumaOrigin">The frame row of the first luma row of the planes (a multiple of 8; <c>lumaOrigin / 2</c> for chroma): the planes hold the chroma rows the row needs.</param>
    /// <param name="y">The row (0 to <c>Height - 1</c>).</param>
    /// <param name="alpha">The alpha values of the row, or empty for opaque pixels (always empty for RGB pixels).</param>
    /// <param name="destination">At least <c>bytesPerPixel * Width</c> bytes.</param>
    /// <param name="scratch">At least <c>2 * Width</c> bytes of scratch space.</param>
    /// <param name="bytesPerPixel">4 for RGBA pixels, 3 for RGB pixels.</param>
    public static void ConvertRow(Vp8Planes planes, int lumaOrigin, int y, ReadOnlySpan<byte> alpha, Span<byte> destination, Span<byte> scratch, int bytesPerPixel)
    {
        var width = planes.Width;
        var upU = scratch[..width];
        var upV = scratch.Slice(width, width);
        UpsampleRow(planes.U, planes.UVStride, planes.Width, planes.Height, lumaOrigin / 2, y, upU);
        UpsampleRow(planes.V, planes.UVStride, planes.Width, planes.Height, lumaOrigin / 2, y, upV);
        var luma = planes.Y.Slice((y - lumaOrigin) * planes.YStride, width);
        if (bytesPerPixel == 3)
        {
            ConvertSamplesRgb(luma, upU, upV, destination[..(width * 3)]);
        }
        else
        {
            ConvertSamples(luma, upU, upV, alpha, destination[..(width * 4)]);
        }
    }

    /// <summary>Converts luma and upsampled chroma samples to RGBA pixels (alpha from <paramref name="alpha"/>, or opaque when it is empty).</summary>
    /// <remarks>
    /// With 128-bit hardware vectors, 16 pixels are converted at a time in 32-bit lanes (256-bit vectors on x64 when they
    /// are accelerated) with the arithmetic of <see cref="ConvertSamplesScalar"/> (the largest term,
    /// 132201 * 127 + 76309 * 239 + 32768, fits 26 bits); the 16 values of a channel are narrowed with unsigned saturation,
    /// which is the clamp, then interleaved.
    /// </remarks>
    internal static void ConvertSamples(ReadOnlySpan<byte> luma, ReadOnlySpan<byte> blue, ReadOnlySpan<byte> red, ReadOnlySpan<byte> alpha, Span<byte> destination)
    {
        var done = UseVectors ? ConvertSamplesVector(luma, blue, red, alpha, destination, bytesPerPixel: 4) : 0;
        ConvertSamplesScalar(luma[done..], blue[done..], red[done..], alpha.IsEmpty ? alpha : alpha[done..], destination[(4 * done)..]);
    }

    /// <summary>Converts luma and upsampled chroma samples to RGB pixels, as <see cref="ConvertSamples"/> without alpha.</summary>
    internal static void ConvertSamplesRgb(ReadOnlySpan<byte> luma, ReadOnlySpan<byte> blue, ReadOnlySpan<byte> red, Span<byte> destination)
    {
        var done = UseVectors ? ConvertSamplesVector(luma, blue, red, default, destination, bytesPerPixel: 3) : 0;
        ConvertSamplesRgbScalar(luma[done..], blue[done..], red[done..], destination[(3 * done)..]);
    }

    /// <summary>The scalar reference of <see cref="ConvertSamples"/>.</summary>
    internal static void ConvertSamplesScalar(ReadOnlySpan<byte> luma, ReadOnlySpan<byte> blue, ReadOnlySpan<byte> red, ReadOnlySpan<byte> alpha, Span<byte> destination)
        => ConvertSamplesCore(luma, blue, red, alpha, destination, bytesPerPixel: 4);

    /// <summary>The scalar reference of <see cref="ConvertSamplesRgb"/>.</summary>
    internal static void ConvertSamplesRgbScalar(ReadOnlySpan<byte> luma, ReadOnlySpan<byte> blue, ReadOnlySpan<byte> red, Span<byte> destination)
        => ConvertSamplesCore(luma, blue, red, default, destination, bytesPerPixel: 3);

    private static void ConvertSamplesCore(ReadOnlySpan<byte> luma, ReadOnlySpan<byte> blue, ReadOnlySpan<byte> red, ReadOnlySpan<byte> alpha, Span<byte> destination, int bytesPerPixel)
    {
        var output = destination[..(luma.Length * bytesPerPixel)];
        blue = blue[..luma.Length];
        red = red[..luma.Length];
        var hasAlpha = !alpha.IsEmpty;
        if (hasAlpha)
        {
            alpha = alpha[..luma.Length];
        }

        var lumaTerm = LumaTerm;
        var redCr = RedCrTerm;
        var greenCb = GreenCbTerm;
        var greenCr = GreenCrTerm;
        var blueCb = BlueCbTerm;
        for (var x = 0; x < luma.Length; x++)
        {
            var c = lumaTerm[luma[x]];
            int cb = blue[x];
            int cr = red[x];
            var pixel = output.Slice(x * bytesPerPixel, bytesPerPixel);
            if (bytesPerPixel == 4)
            {
                pixel[3] = hasAlpha ? alpha[x] : byte.MaxValue;
            }

            pixel[2] = Clamp((c + blueCb[cb]) >> 16);
            pixel[1] = Clamp((c - greenCb[cb] - greenCr[cr]) >> 16);
            pixel[0] = Clamp((c + redCr[cr]) >> 16);
        }
    }

    /// <summary>Upsamples one chroma plane, which holds the frame rows from <paramref name="chromaOrigin"/>, to the luma row <paramref name="y"/> (centered bilinear 9-3-3-1 weights).</summary>
    public static void UpsampleRow(ReadOnlySpan<byte> plane, int stride, int width, int height, int chromaOrigin, int y, Span<byte> destination)
    {
        var chromaWidth = (width + 1) >> 1;
        var chromaHeight = (height + 1) >> 1;
        var near = y >> 1;
        var far = (y & 1) == 0 ? Math.Max(near - 1, 0) : Math.Min(near + 1, chromaHeight - 1);
        UpsampleSamples(plane.Slice((near - chromaOrigin) * stride, chromaWidth), plane.Slice((far - chromaOrigin) * stride, chromaWidth), destination[..width]);
    }

    /// <summary>
    /// Upsamples a chroma row horizontally and blends it with its vertical neighbor: <paramref name="destination"/> has
    /// <c>2 * nearRow.Length</c> or <c>2 * nearRow.Length - 1</c> samples.
    /// </summary>
    /// <remarks>
    /// With 128-bit hardware vectors, 16 chroma samples that have both neighbors give 32 output samples at a time, in 16-bit
    /// lanes (the largest sum, 16 * 255 + 8, fits 12 bits); the result is the one of <see cref="UpsampleSamplesScalar"/>.
    /// </remarks>
    internal static void UpsampleSamples(ReadOnlySpan<byte> nearRow, ReadOnlySpan<byte> farRow, Span<byte> destination)
    {
        if (UseVectors && nearRow.Length >= 18 && farRow.Length >= nearRow.Length && destination.Length >= (2 * nearRow.Length) - 1)
        {
            // Samples 1 to Length - 2 have both neighbors; the vectors read one sample before and after their 16 samples
            UpsampleSamplesVector(nearRow, farRow, destination);
            UpsampleSamplesScalar(nearRow, farRow, destination, 0, 1);
            UpsampleSamplesScalar(nearRow, farRow, destination, nearRow.Length - 1, 1);
            return;
        }

        UpsampleSamplesScalar(nearRow, farRow, destination, 0, nearRow.Length);
    }

    /// <summary>The scalar reference of <see cref="UpsampleSamples"/>, for the chroma samples <paramref name="first"/> to <paramref name="first"/> + <paramref name="count"/> - 1.</summary>
    internal static void UpsampleSamplesScalar(ReadOnlySpan<byte> nearRow, ReadOnlySpan<byte> farRow, Span<byte> destination, int first, int count)
    {
        // Each chroma sample i contributes to the luma pixels 2i (with neighbor i - 1) and 2i + 1 (with neighbor i + 1)
        var chromaWidth = nearRow.Length;
        for (var i = first; i < first + count; i++)
        {
            var center = (3 * nearRow[i]) + farRow[i];
            var left = i > 0 ? (3 * nearRow[i - 1]) + farRow[i - 1] : center;
            var right = i + 1 < chromaWidth ? (3 * nearRow[i + 1]) + farRow[i + 1] : center;
            var x = 2 * i;
            destination[x] = (byte)(((3 * center) + left + 8) >> 4);
            if (x + 1 < destination.Length)
            {
                destination[x + 1] = (byte)(((3 * center) + right + 8) >> 4);
            }
        }
    }

    private static bool UseVectors => Vector128.IsHardwareAccelerated && BitConverter.IsLittleEndian && (AdvSimd.Arm64.IsSupported || Ssse3.IsSupported);

    /// <summary>Upsamples the chroma samples 1 to <c>nearRow.Length - 2</c> (at least 16), 16 at a time: the last group overlaps the previous one.</summary>
    private static void UpsampleSamplesVector(ReadOnlySpan<byte> nearRow, ReadOnlySpan<byte> farRow, Span<byte> destination)
    {
        ref var near = ref unsafe(MemoryMarshal.GetReference(nearRow));
        ref var far = ref unsafe(MemoryMarshal.GetReference(farRow));
        ref var output = ref unsafe(MemoryMarshal.GetReference(destination));
        var three = Vector128.Create((ushort)3);
        var eight = Vector128.Create((ushort)8);

        // The group of samples i to i + 15 reads the samples i - 1 to i + 16 and writes the output samples 2 i to 2 i + 31
        var last = nearRow.Length - 17;
        for (var i = 1; ; i += 16)
        {
            i = Math.Min(i, last);

            // 3 near + far of the samples i - 1 to i + 14 (before) and i + 1 to i + 16 (after), in 16-bit lanes
            var (beforeLow, beforeHigh) = Blend(ref near, ref far, i - 1, three);
            var (afterLow, afterHigh) = Blend(ref near, ref far, i + 1, three);
            var center = ShiftLanes(beforeLow, beforeHigh, 1);
            var weighted = (center * three) + eight;
            var pairs = Interleave((weighted + beforeLow) >> 4, (weighted + afterLow) >> 4);
            unsafe { pairs.StoreUnsafe(ref output, (nuint)(2 * i)); }
            center = ShiftLanes(afterLow, afterHigh, 7);
            weighted = (center * three) + eight;
            pairs = Interleave((weighted + beforeHigh) >> 4, (weighted + afterHigh) >> 4);
            unsafe { pairs.StoreUnsafe(ref output, (nuint)((2 * i) + 16)); }
            if (i == last)
                break;
        }

        // 3 near + far for 16 samples, in two vectors of 16-bit lanes
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static (Vector128<ushort> Low, Vector128<ushort> High) Blend(ref byte near, ref byte far, int index, Vector128<ushort> three)
        {
            var nearSamples = unsafe(Vector128.LoadUnsafe(ref near, (nuint)index));
            var farSamples = unsafe(Vector128.LoadUnsafe(ref far, (nuint)index));
            return ((Vector128.WidenLower(nearSamples) * three) + Vector128.WidenLower(farSamples), (Vector128.WidenUpper(nearSamples) * three) + Vector128.WidenUpper(farSamples));
        }

        // Lanes count to count + 7 of the 16 lanes of low and high (count is 1 or 7)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Vector128<ushort> ShiftLanes(Vector128<ushort> low, Vector128<ushort> high, [ConstantExpected(Min = 1, Max = 7)] byte count)
        {
            if (AdvSimd.IsSupported)
                return AdvSimd.ExtractVector128(low, high, count);

            return (count == 1 ? Ssse3.AlignRight(high.AsByte(), low.AsByte(), 2) : Ssse3.AlignRight(high.AsByte(), low.AsByte(), 14)).AsUInt16();
        }

        // The even samples in the low bytes of the lanes, the odd samples in the high bytes (both below 256)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Vector128<byte> Interleave(Vector128<ushort> even, Vector128<ushort> odd)
        {
            if (AdvSimd.IsSupported)
                return AdvSimd.ShiftLeftAndInsert(even, odd, 8).AsByte();

            return (even | (odd << 8)).AsByte();
        }
    }

    // The constant terms of the matrix, folded: (76309 (Y - 16) + 104597 (Cr - 128) + 32768) is 76309 Y + 104597 Cr + RedOffset
    private const int RedOffset = 32768 - (16 * LumaScale) - (128 * RedFromCr);
    private const int GreenOffset = 32768 - (16 * LumaScale) + (128 * GreenFromCb) + (128 * GreenFromCr);
    private const int BlueOffset = 32768 - (16 * LumaScale) - (128 * BlueFromCb);

    /// <summary>Converts whole groups of 16 pixels to RGBA (4 bytes per pixel) or RGB (3 bytes per pixel).</summary>
    /// <returns>The number of pixels converted.</returns>
    private static int ConvertSamplesVector(ReadOnlySpan<byte> luma, ReadOnlySpan<byte> blue, ReadOnlySpan<byte> red, ReadOnlySpan<byte> alpha, Span<byte> destination, int bytesPerPixel)
    {
        var length = Math.Min(luma.Length, Math.Min(blue.Length, Math.Min(red.Length, destination.Length / bytesPerPixel)));
        var hasAlpha = !alpha.IsEmpty;
        if (hasAlpha)
        {
            length = Math.Min(length, alpha.Length);
        }

        ref var lumaStart = ref unsafe(MemoryMarshal.GetReference(luma));
        ref var blueStart = ref unsafe(MemoryMarshal.GetReference(blue));
        ref var redStart = ref unsafe(MemoryMarshal.GetReference(red));
        ref var alphaStart = ref unsafe(MemoryMarshal.GetReference(alpha));
        ref var output = ref unsafe(MemoryMarshal.GetReference(destination));
        var opaque = Vector128.Create(byte.MaxValue);

        // The constants of Convert16AdvSimd, created once
        var coefficients = Vector128.Create((ushort)LumaRest, RedFromCrRest, GreenFromCb, GreenFromCr, BlueFromCbRest, 0, 0, 0);
        var redOffset = Vector128.Create(unchecked((uint)RedOffset));
        var greenOffset = Vector128.Create((uint)GreenOffset);
        var blueOffset = Vector128.Create(unchecked((uint)BlueOffset));
        var x = 0;
        for (; x + 16 <= length; x += 16)
        {
            var lumaValues = unsafe(Vector128.LoadUnsafe(ref lumaStart, (nuint)x));
            var blueValues = unsafe(Vector128.LoadUnsafe(ref blueStart, (nuint)x));
            var redValues = unsafe(Vector128.LoadUnsafe(ref redStart, (nuint)x));
            Vector128<byte> r;
            Vector128<byte> g;
            Vector128<byte> b;
            if (Vector256.IsHardwareAccelerated && Avx2.IsSupported)
            {
                (r, g, b) = Convert16Avx2(lumaValues, blueValues, redValues);
            }
            else if (AdvSimd.Arm64.IsSupported)
            {
                (r, g, b) = Convert16AdvSimd(lumaValues, blueValues, redValues, coefficients, redOffset, greenOffset, blueOffset);
            }
            else
            {
                (r, g, b) = Convert16(lumaValues, blueValues, redValues);
            }

            if (bytesPerPixel == 3)
            {
                StoreRgb(r, g, b, ref unsafe(Unsafe.Add(ref output, 3 * x)));
            }
            else
            {
                var a = hasAlpha ? unsafe(Vector128.LoadUnsafe(ref alphaStart, (nuint)x)) : opaque;
                StoreRgba(r, g, b, a, ref unsafe(Unsafe.Add(ref output, 4 * x)));
            }
        }

        return x;
    }

    /// <summary>The red, green and blue values of 16 pixels, in 4 groups of 32-bit lanes.</summary>
    private static (Vector128<byte> Red, Vector128<byte> Green, Vector128<byte> Blue) Convert16(Vector128<byte> luma, Vector128<byte> blue, Vector128<byte> red)
    {
        var (y0, y1) = Vector128.Widen(luma);
        var (b0, b1) = Vector128.Widen(blue);
        var (r0, r1) = Vector128.Widen(red);
        var (red0, green0, blue0) = Convert4(Vector128.WidenLower(y0).AsInt32(), Vector128.WidenLower(b0).AsInt32(), Vector128.WidenLower(r0).AsInt32());
        var (red1, green1, blue1) = Convert4(Vector128.WidenUpper(y0).AsInt32(), Vector128.WidenUpper(b0).AsInt32(), Vector128.WidenUpper(r0).AsInt32());
        var (red2, green2, blue2) = Convert4(Vector128.WidenLower(y1).AsInt32(), Vector128.WidenLower(b1).AsInt32(), Vector128.WidenLower(r1).AsInt32());
        var (red3, green3, blue3) = Convert4(Vector128.WidenUpper(y1).AsInt32(), Vector128.WidenUpper(b1).AsInt32(), Vector128.WidenUpper(r1).AsInt32());

        // The values are between -223 and 480: narrowing to 16 bits is exact, the narrowing to bytes saturates
        return (
            PackUnsignedSaturate(Vector128.Narrow(red0, red1), Vector128.Narrow(red2, red3)),
            PackUnsignedSaturate(Vector128.Narrow(green0, green1), Vector128.Narrow(green2, green3)),
            PackUnsignedSaturate(Vector128.Narrow(blue0, blue1), Vector128.Narrow(blue2, blue3)));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static (Vector128<int> Red, Vector128<int> Green, Vector128<int> Blue) Convert4(Vector128<int> lumaValues, Vector128<int> blueValues, Vector128<int> redValues)
        {
            var c = lumaValues * Vector128.Create(LumaScale);
            var r = (c + ((redValues * Vector128.Create(RedFromCr)) + Vector128.Create(RedOffset))) >> 16;
            var g = (c - (blueValues * Vector128.Create(GreenFromCb)) - (redValues * Vector128.Create(GreenFromCr)) + Vector128.Create(GreenOffset)) >> 16;
            var b = (c + ((blueValues * Vector128.Create(BlueFromCb)) + Vector128.Create(BlueOffset))) >> 16;
            return (r, g, b);
        }
    }

    // The coefficients split into multiples of 65536 and a 16-bit rest: 76309 = 65536 + 10773, 104597 = 65536 + 39061 and
    // 132201 = 2 * 65536 + 1129
    private const int LumaRest = LumaScale - 65536;
    private const int RedFromCrRest = RedFromCr - 65536;
    private const int BlueFromCbRest = BlueFromCb - (2 * 65536);

    /// <summary>The red, green and blue values of 16 pixels, with 16-bit multiplications that accumulate in 32-bit lanes.</summary>
    /// <remarks>
    /// The multiples of 65536 of the coefficients add whole values after the shift, which are added in 16-bit lanes:
    /// <c>R = Y + Cr + ((10773 Y + 39061 Cr + RedOffset) &gt;&gt; 16)</c>,
    /// <c>G = Y + ((10773 Y - 25675 Cb - 53279 Cr + GreenOffset) &gt;&gt; 16)</c> and
    /// <c>B = Y + 2 Cb + ((10773 Y + 1129 Cb + BlueOffset) &gt;&gt; 16)</c>, the exact results of <see cref="ConvertSamplesScalar"/>.
    /// The sums fit 32-bit lanes (they wrap as unsigned values, their signed values are below 2^24), the shifted sums are
    /// between -277 and 178 and the results between -277 and 534: the high halves of the 32-bit lanes (UZP2) are the shifted
    /// sums, and the 16-bit sums are exact before the narrowing with unsigned saturation, which is the clamp.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (Vector128<byte> Red, Vector128<byte> Green, Vector128<byte> Blue) Convert16AdvSimd(Vector128<byte> luma, Vector128<byte> blue, Vector128<byte> red, Vector128<ushort> coefficients, Vector128<uint> redOffset, Vector128<uint> greenOffset, Vector128<uint> blueOffset)
    {
        // The coefficients: lane 0 LumaRest, 1 RedFromCrRest, 2 GreenFromCb, 3 GreenFromCr, 4 BlueFromCbRest
        var (red0, green0, blue0) = Convert8(Vector128.WidenLower(luma), Vector128.WidenLower(blue), Vector128.WidenLower(red), coefficients, redOffset, greenOffset, blueOffset);
        var (red1, green1, blue1) = Convert8(Vector128.WidenUpper(luma), Vector128.WidenUpper(blue), Vector128.WidenUpper(red), coefficients, redOffset, greenOffset, blueOffset);
        return (PackUnsignedSaturate(red0, red1), PackUnsignedSaturate(green0, green1), PackUnsignedSaturate(blue0, blue1));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static (Vector128<short> Red, Vector128<short> Green, Vector128<short> Blue) Convert8(Vector128<ushort> y, Vector128<ushort> cb, Vector128<ushort> cr, Vector128<ushort> k, Vector128<uint> redOffset, Vector128<uint> greenOffset, Vector128<uint> blueOffset)
        {
            var redLow = AdvSimd.MultiplyBySelectedScalarWideningLowerAndAdd(AdvSimd.MultiplyBySelectedScalarWideningLowerAndAdd(redOffset, y.GetLower(), k, 0), cr.GetLower(), k, 1);
            var redHigh = AdvSimd.MultiplyBySelectedScalarWideningUpperAndAdd(AdvSimd.MultiplyBySelectedScalarWideningUpperAndAdd(redOffset, y, k, 0), cr, k, 1);
            var greenLow = AdvSimd.MultiplyBySelectedScalarWideningLowerAndSubtract(AdvSimd.MultiplyBySelectedScalarWideningLowerAndSubtract(AdvSimd.MultiplyBySelectedScalarWideningLowerAndAdd(greenOffset, y.GetLower(), k, 0), cb.GetLower(), k, 2), cr.GetLower(), k, 3);
            var greenHigh = AdvSimd.MultiplyBySelectedScalarWideningUpperAndSubtract(AdvSimd.MultiplyBySelectedScalarWideningUpperAndSubtract(AdvSimd.MultiplyBySelectedScalarWideningUpperAndAdd(greenOffset, y, k, 0), cb, k, 2), cr, k, 3);
            var blueLow = AdvSimd.MultiplyBySelectedScalarWideningLowerAndAdd(AdvSimd.MultiplyBySelectedScalarWideningLowerAndAdd(blueOffset, y.GetLower(), k, 0), cb.GetLower(), k, 4);
            var blueHigh = AdvSimd.MultiplyBySelectedScalarWideningUpperAndAdd(AdvSimd.MultiplyBySelectedScalarWideningUpperAndAdd(blueOffset, y, k, 0), cb, k, 4);
            var signedY = y.AsInt16();
            var r = signedY + cr.AsInt16() + AdvSimd.Arm64.UnzipOdd(redLow.AsInt16(), redHigh.AsInt16());
            var g = signedY + AdvSimd.Arm64.UnzipOdd(greenLow.AsInt16(), greenHigh.AsInt16());
            var b = signedY + cb.AsInt16() + cb.AsInt16() + AdvSimd.Arm64.UnzipOdd(blueLow.AsInt16(), blueHigh.AsInt16());
            return (r, g, b);
        }
    }

    /// <summary>The red, green and blue values of 16 pixels, in 2 groups of 32-bit lanes of 256-bit vectors.</summary>
    private static (Vector128<byte> Red, Vector128<byte> Green, Vector128<byte> Blue) Convert16Avx2(Vector128<byte> luma, Vector128<byte> blue, Vector128<byte> red)
    {
        var (red0, green0, blue0) = Convert8(Avx2.ConvertToVector256Int32(luma), Avx2.ConvertToVector256Int32(blue), Avx2.ConvertToVector256Int32(red));
        var (red1, green1, blue1) = Convert8(Avx2.ConvertToVector256Int32(Sse2.ShiftRightLogical128BitLane(luma, 8)), Avx2.ConvertToVector256Int32(Sse2.ShiftRightLogical128BitLane(blue, 8)), Avx2.ConvertToVector256Int32(Sse2.ShiftRightLogical128BitLane(red, 8)));
        return (Pack(red0, red1), Pack(green0, green1), Pack(blue0, blue1));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static (Vector256<int> Red, Vector256<int> Green, Vector256<int> Blue) Convert8(Vector256<int> lumaValues, Vector256<int> blueValues, Vector256<int> redValues)
        {
            var c = lumaValues * Vector256.Create(LumaScale);
            var r = (c + ((redValues * Vector256.Create(RedFromCr)) + Vector256.Create(RedOffset))) >> 16;
            var g = (c - (blueValues * Vector256.Create(GreenFromCb)) - (redValues * Vector256.Create(GreenFromCr)) + Vector256.Create(GreenOffset)) >> 16;
            var b = (c + ((blueValues * Vector256.Create(BlueFromCb)) + Vector256.Create(BlueOffset))) >> 16;
            return (r, g, b);
        }

        // 16 values to bytes: the 16-bit pack works per 128-bit lane, its 64-bit groups hold the values 0-3, 8-11, 4-7, 12-15
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Vector128<byte> Pack(Vector256<int> low, Vector256<int> high)
        {
            var words = Avx2.Permute4x64(Avx2.PackSignedSaturate(low, high).AsInt64(), 0b11_01_10_00).AsInt16();
            return Sse2.PackUnsignedSaturate(words.GetLower(), words.GetUpper());
        }
    }

    /// <summary>Narrows signed 16-bit values to bytes with unsigned saturation (0 to 255).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<byte> PackUnsignedSaturate(Vector128<short> low, Vector128<short> high)
    {
        if (AdvSimd.IsSupported)
            return AdvSimd.ExtractNarrowingSaturateUnsignedUpper(AdvSimd.ExtractNarrowingSaturateUnsignedLower(low), high);

        return Sse2.PackUnsignedSaturate(low, high);
    }

    /// <summary>Interleaves 16 red, green and blue values into 48 bytes.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void StoreRgb(Vector128<byte> r, Vector128<byte> g, Vector128<byte> b, ref byte destination)
    {
        Vector128<byte> first;
        Vector128<byte> second;
        Vector128<byte> third;
        if (AdvSimd.Arm64.IsSupported)
        {
            // Table indices: 0-15 red, 16-31 green, 32-47 blue
            first = AdvSimd.Arm64.VectorTableLookup((r, g, b), Vector128.Create((byte)0, 16, 32, 1, 17, 33, 2, 18, 34, 3, 19, 35, 4, 20, 36, 5));
            second = AdvSimd.Arm64.VectorTableLookup((r, g, b), Vector128.Create((byte)21, 37, 6, 22, 38, 7, 23, 39, 8, 24, 40, 9, 25, 41, 10, 26));
            third = AdvSimd.Arm64.VectorTableLookup((r, g, b), Vector128.Create((byte)42, 11, 27, 43, 12, 28, 44, 13, 29, 45, 14, 30, 46, 15, 31, 47));
        }
        else
        {
            // 0x80 selects zero
            first = Ssse3.Shuffle(r, Vector128.Create((byte)0, 0x80, 0x80, 1, 0x80, 0x80, 2, 0x80, 0x80, 3, 0x80, 0x80, 4, 0x80, 0x80, 5))
                | Ssse3.Shuffle(g, Vector128.Create((byte)0x80, 0, 0x80, 0x80, 1, 0x80, 0x80, 2, 0x80, 0x80, 3, 0x80, 0x80, 4, 0x80, 0x80))
                | Ssse3.Shuffle(b, Vector128.Create((byte)0x80, 0x80, 0, 0x80, 0x80, 1, 0x80, 0x80, 2, 0x80, 0x80, 3, 0x80, 0x80, 4, 0x80));
            second = Ssse3.Shuffle(r, Vector128.Create((byte)0x80, 0x80, 6, 0x80, 0x80, 7, 0x80, 0x80, 8, 0x80, 0x80, 9, 0x80, 0x80, 10, 0x80))
                | Ssse3.Shuffle(g, Vector128.Create((byte)5, 0x80, 0x80, 6, 0x80, 0x80, 7, 0x80, 0x80, 8, 0x80, 0x80, 9, 0x80, 0x80, 10))
                | Ssse3.Shuffle(b, Vector128.Create((byte)0x80, 5, 0x80, 0x80, 6, 0x80, 0x80, 7, 0x80, 0x80, 8, 0x80, 0x80, 9, 0x80, 0x80));
            third = Ssse3.Shuffle(r, Vector128.Create((byte)0x80, 11, 0x80, 0x80, 12, 0x80, 0x80, 13, 0x80, 0x80, 14, 0x80, 0x80, 15, 0x80, 0x80))
                | Ssse3.Shuffle(g, Vector128.Create((byte)0x80, 0x80, 11, 0x80, 0x80, 12, 0x80, 0x80, 13, 0x80, 0x80, 14, 0x80, 0x80, 15, 0x80))
                | Ssse3.Shuffle(b, Vector128.Create((byte)10, 0x80, 0x80, 11, 0x80, 0x80, 12, 0x80, 0x80, 13, 0x80, 0x80, 14, 0x80, 0x80, 15));
        }

        unsafe
        {
            first.StoreUnsafe(ref destination);
            second.StoreUnsafe(ref destination, 16);
            third.StoreUnsafe(ref destination, 32);
        }
    }

    /// <summary>Interleaves 16 red, green, blue and alpha values into 64 bytes.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void StoreRgba(Vector128<byte> r, Vector128<byte> g, Vector128<byte> b, Vector128<byte> a, ref byte destination)
    {
        Vector128<ushort> redGreenLow;
        Vector128<ushort> redGreenHigh;
        Vector128<ushort> blueAlphaLow;
        Vector128<ushort> blueAlphaHigh;
        Vector128<byte> first;
        Vector128<byte> second;
        Vector128<byte> third;
        Vector128<byte> fourth;
        if (AdvSimd.Arm64.IsSupported)
        {
            redGreenLow = AdvSimd.Arm64.ZipLow(r, g).AsUInt16();
            redGreenHigh = AdvSimd.Arm64.ZipHigh(r, g).AsUInt16();
            blueAlphaLow = AdvSimd.Arm64.ZipLow(b, a).AsUInt16();
            blueAlphaHigh = AdvSimd.Arm64.ZipHigh(b, a).AsUInt16();
            first = AdvSimd.Arm64.ZipLow(redGreenLow, blueAlphaLow).AsByte();
            second = AdvSimd.Arm64.ZipHigh(redGreenLow, blueAlphaLow).AsByte();
            third = AdvSimd.Arm64.ZipLow(redGreenHigh, blueAlphaHigh).AsByte();
            fourth = AdvSimd.Arm64.ZipHigh(redGreenHigh, blueAlphaHigh).AsByte();
        }
        else
        {
            redGreenLow = Sse2.UnpackLow(r, g).AsUInt16();
            redGreenHigh = Sse2.UnpackHigh(r, g).AsUInt16();
            blueAlphaLow = Sse2.UnpackLow(b, a).AsUInt16();
            blueAlphaHigh = Sse2.UnpackHigh(b, a).AsUInt16();
            first = Sse2.UnpackLow(redGreenLow, blueAlphaLow).AsByte();
            second = Sse2.UnpackHigh(redGreenLow, blueAlphaLow).AsByte();
            third = Sse2.UnpackLow(redGreenHigh, blueAlphaHigh).AsByte();
            fourth = Sse2.UnpackHigh(redGreenHigh, blueAlphaHigh).AsByte();
        }

        unsafe
        {
            first.StoreUnsafe(ref destination);
            second.StoreUnsafe(ref destination, 16);
            third.StoreUnsafe(ref destination, 32);
            fourth.StoreUnsafe(ref destination, 48);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte Clamp(int value) => (uint)value <= 255 ? (byte)value : value < 0 ? (byte)0 : byte.MaxValue;

    private static int[] CreateTable(Func<int, int> term)
    {
        var table = new int[256];
        for (var v = 0; v < table.Length; v++)
        {
            table[v] = term(v);
        }

        return table;
    }
}
