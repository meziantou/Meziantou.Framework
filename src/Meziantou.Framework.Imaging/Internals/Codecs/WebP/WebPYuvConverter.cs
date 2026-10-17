using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

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

    /// <summary>Converts row <paramref name="y"/> of the planes to RGBA (alpha from <paramref name="alpha"/>, or opaque).</summary>
    /// <param name="planes">The reconstructed planes.</param>
    /// <param name="y">The row (0 to <c>Height - 1</c>).</param>
    /// <param name="alpha">The alpha values of the row, or empty for opaque pixels.</param>
    /// <param name="destination">At least <c>4 * Width</c> bytes.</param>
    /// <param name="scratch">At least <c>2 * Width</c> bytes of scratch space.</param>
    public static void ConvertRow(Vp8Planes planes, int y, ReadOnlySpan<byte> alpha, Span<byte> destination, Span<byte> scratch)
    {
        var width = planes.Width;
        var upU = scratch[..width];
        var upV = scratch.Slice(width, width);
        UpsampleRow(planes.U, planes.UVStride, planes.Width, planes.Height, y, upU);
        UpsampleRow(planes.V, planes.UVStride, planes.Width, planes.Height, y, upV);
        var luma = planes.Y.Slice(y * planes.YStride, width);
        ConvertSamples(luma, upU, upV, alpha, destination[..(width * 4)]);
    }

    /// <summary>Converts luma and upsampled chroma samples to RGBA pixels (alpha from <paramref name="alpha"/>, or opaque when it is empty).</summary>
    /// <remarks>
    /// With 128-bit hardware vectors, 16 pixels are converted at a time in 32-bit lanes with the arithmetic of
    /// <see cref="ConvertSamplesScalar"/> (the largest term, 132201 * 127 + 76309 * 239 + 32768, fits 26 bits).
    /// </remarks>
    internal static void ConvertSamples(ReadOnlySpan<byte> luma, ReadOnlySpan<byte> blue, ReadOnlySpan<byte> red, ReadOnlySpan<byte> alpha, Span<byte> destination)
    {
        var done = UseVectors ? ConvertSamplesVector(luma, blue, red, alpha, destination) : 0;
        ConvertSamplesScalar(luma[done..], blue[done..], red[done..], alpha.IsEmpty ? alpha : alpha[done..], destination[(4 * done)..]);
    }

    /// <summary>The scalar reference of <see cref="ConvertSamples"/>.</summary>
    internal static void ConvertSamplesScalar(ReadOnlySpan<byte> luma, ReadOnlySpan<byte> blue, ReadOnlySpan<byte> red, ReadOnlySpan<byte> alpha, Span<byte> destination)
    {
        var output = destination[..(luma.Length * 4)];
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
            var pixel = output.Slice(x * 4, 4);
            pixel[3] = hasAlpha ? alpha[x] : byte.MaxValue;
            pixel[2] = Clamp((c + blueCb[cb]) >> 16);
            pixel[1] = Clamp((c - greenCb[cb] - greenCr[cr]) >> 16);
            pixel[0] = Clamp((c + redCr[cr]) >> 16);
        }
    }

    /// <summary>Upsamples one chroma plane to the luma row <paramref name="y"/> (centered bilinear 9-3-3-1 weights).</summary>
    public static void UpsampleRow(ReadOnlySpan<byte> plane, int stride, int width, int height, int y, Span<byte> destination)
    {
        var chromaWidth = (width + 1) >> 1;
        var chromaHeight = (height + 1) >> 1;
        var near = y >> 1;
        var far = (y & 1) == 0 ? Math.Max(near - 1, 0) : Math.Min(near + 1, chromaHeight - 1);
        UpsampleSamples(plane.Slice(near * stride, chromaWidth), plane.Slice(far * stride, chromaWidth), destination[..width]);
    }

    /// <summary>
    /// Upsamples a chroma row horizontally and blends it with its vertical neighbor: <paramref name="destination"/> has
    /// <c>2 * nearRow.Length</c> or <c>2 * nearRow.Length - 1</c> samples.
    /// </summary>
    /// <remarks>
    /// With 128-bit hardware vectors, 8 chroma samples that have both neighbors give 16 output samples at a time, in 16-bit
    /// lanes (the largest sum, 16 * 255 + 8, fits 12 bits); the result is the one of <see cref="UpsampleSamplesScalar"/>.
    /// </remarks>
    internal static void UpsampleSamples(ReadOnlySpan<byte> nearRow, ReadOnlySpan<byte> farRow, Span<byte> destination)
    {
        var first = 0;
        var count = 0;
        if (UseVectors && nearRow.Length >= 10 && farRow.Length >= nearRow.Length && destination.Length >= (2 * nearRow.Length) - 1)
        {
            // Samples 1 to Length - 2 have both neighbors; the vectors read one sample before and after their 8 samples
            first = 1;
            count = (nearRow.Length - 2) & ~7;
            UpsampleSamplesVector(nearRow, farRow, destination, first, count);
        }

        UpsampleSamplesScalar(nearRow, farRow, destination, 0, first);
        UpsampleSamplesScalar(nearRow, farRow, destination, first + count, nearRow.Length - first - count);
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

    private static bool UseVectors => Vector128.IsHardwareAccelerated && BitConverter.IsLittleEndian;

    private static void UpsampleSamplesVector(ReadOnlySpan<byte> nearRow, ReadOnlySpan<byte> farRow, Span<byte> destination, int first, int count)
    {
        ref var near = ref unsafe(MemoryMarshal.GetReference(nearRow));
        ref var far = ref unsafe(MemoryMarshal.GetReference(farRow));
        ref var output = ref unsafe(MemoryMarshal.GetReference(destination));
        var three = Vector128.Create((ushort)3);
        var eight = Vector128.Create((ushort)8);
        for (var i = first; i < first + count; i += 8)
        {
            var center = Blend(ref near, ref far, i, three);
            var weighted = (center * three) + eight;
            var even = (weighted + Blend(ref near, ref far, i - 1, three)) >> 4;
            var odd = (weighted + Blend(ref near, ref far, i + 1, three)) >> 4;

            // The even and the odd samples, interleaved: each lane holds an even sample in its low byte and the next odd one
            unsafe { (even | (odd << 8)).AsByte().StoreUnsafe(ref output, (nuint)(2 * i)); }
        }

        // 3 near + far for 8 samples, in 16-bit lanes
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Vector128<ushort> Blend(ref byte near, ref byte far, int index, Vector128<ushort> three)
        {
            var nearSamples = Vector128.WidenLower(Vector128.CreateScalar(unsafe(Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref near, index)))).AsByte());
            var farSamples = Vector128.WidenLower(Vector128.CreateScalar(unsafe(Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref far, index)))).AsByte());
            return (nearSamples * three) + farSamples;
        }
    }

    /// <summary>Converts whole groups of 16 pixels.</summary>
    /// <returns>The number of pixels converted.</returns>
    private static int ConvertSamplesVector(ReadOnlySpan<byte> luma, ReadOnlySpan<byte> blue, ReadOnlySpan<byte> red, ReadOnlySpan<byte> alpha, Span<byte> destination)
    {
        var length = Math.Min(luma.Length, Math.Min(blue.Length, Math.Min(red.Length, destination.Length / 4)));
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
        var x = 0;
        for (; x + 16 <= length; x += 16)
        {
            var (y0, y1) = Vector128.Widen(unsafe(Vector128.LoadUnsafe(ref lumaStart, (nuint)x)));
            var (b0, b1) = Vector128.Widen(unsafe(Vector128.LoadUnsafe(ref blueStart, (nuint)x)));
            var (r0, r1) = Vector128.Widen(unsafe(Vector128.LoadUnsafe(ref redStart, (nuint)x)));
            var (a0, a1) = Vector128.Widen(hasAlpha ? unsafe(Vector128.LoadUnsafe(ref alphaStart, (nuint)x)) : opaque);
            ref var pixels = ref unsafe(Unsafe.Add(ref output, 4 * x));
            unsafe { Convert4(Vector128.WidenLower(y0), Vector128.WidenLower(b0), Vector128.WidenLower(r0), Vector128.WidenLower(a0)).StoreUnsafe(ref pixels); }
            unsafe { Convert4(Vector128.WidenUpper(y0), Vector128.WidenUpper(b0), Vector128.WidenUpper(r0), Vector128.WidenUpper(a0)).StoreUnsafe(ref Unsafe.Add(ref pixels, 16)); }
            unsafe { Convert4(Vector128.WidenLower(y1), Vector128.WidenLower(b1), Vector128.WidenLower(r1), Vector128.WidenLower(a1)).StoreUnsafe(ref Unsafe.Add(ref pixels, 32)); }
            unsafe { Convert4(Vector128.WidenUpper(y1), Vector128.WidenUpper(b1), Vector128.WidenUpper(r1), Vector128.WidenUpper(a1)).StoreUnsafe(ref Unsafe.Add(ref pixels, 48)); }
        }

        return x;

        // 4 pixels as R | G << 8 | B << 16 | A << 24 in 32-bit lanes: their bytes are the RGBA pixels
        static Vector128<byte> Convert4(Vector128<uint> lumaValues, Vector128<uint> blueValues, Vector128<uint> redValues, Vector128<uint> alphaValues)
        {
            var c = ((lumaValues.AsInt32() - Vector128.Create(16)) * Vector128.Create(LumaScale)) + Vector128.Create(32768);
            var cb = blueValues.AsInt32() - Vector128.Create(128);
            var cr = redValues.AsInt32() - Vector128.Create(128);
            var r = (c + (cr * Vector128.Create(RedFromCr))) >> 16;
            var g = (c - (cb * Vector128.Create(GreenFromCb)) - (cr * Vector128.Create(GreenFromCr))) >> 16;
            var b = (c + (cb * Vector128.Create(BlueFromCb))) >> 16;
            var zero = Vector128<int>.Zero;
            var max = Vector128.Create(255);
            r = Vector128.Min(Vector128.Max(r, zero), max);
            g = Vector128.Min(Vector128.Max(g, zero), max);
            b = Vector128.Min(Vector128.Max(b, zero), max);
            return (r | (g << 8) | (b << 16) | (alphaValues.AsInt32() << 24)).AsByte();
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
