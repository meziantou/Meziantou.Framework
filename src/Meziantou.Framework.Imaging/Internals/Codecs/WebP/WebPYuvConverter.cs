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
        var output = destination[..(width * 4)];
        var hasAlpha = !alpha.IsEmpty;
        if (hasAlpha)
        {
            alpha = alpha[..width];
        }

        var lumaTerm = LumaTerm;
        var redCr = RedCrTerm;
        var greenCb = GreenCbTerm;
        var greenCr = GreenCrTerm;
        var blueCb = BlueCbTerm;
        for (var x = 0; x < luma.Length; x++)
        {
            var c = lumaTerm[luma[x]];
            int cb = upU[x];
            int cr = upV[x];
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
        var nearRow = plane.Slice(near * stride, chromaWidth);
        var farRow = plane.Slice(far * stride, chromaWidth);
        // Each chroma sample i contributes to the luma pixels 2i (with neighbor i - 1) and 2i + 1 (with neighbor i + 1)
        destination = destination[..width];
        for (var i = 0; i < chromaWidth; i++)
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

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
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
