using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The "sharp YUV" refinement of <see cref="Formats.WebPEncoder.SharpYuv"/>: corrects the Y'CbCr 4:2:0 planes of a lossy
/// frame so that the pixels the decoder reconstructs from them are closer to the source.
/// </summary>
/// <remarks>
/// <para>
/// RFC 6386 defines the VP8 planes, not how they become RGB: the WebP container recommends BT.601, and the decoders upsample
/// chroma bilinearly with centered siting (<see cref="WebPYuvConverter"/>, the arithmetic of libwebp and the browsers). This
/// refinement simulates that reconstruction in floating point (upsampling, the limited-range matrix and the clamping to 0 to
/// 255) and runs <see cref="Passes"/> passes, each of two steps:
/// </para>
/// <list type="number">
/// <item><description>
/// Chroma: the error of every visible pixel (source minus reconstruction) is projected on Cb and Cr with the forward BT.601
/// matrix, and each chroma sample moves by the average of the projections of the pixels it contributes to, weighted by its
/// upsampling weights (9, 3, 3 and 1 sixteenths) and by the alpha of the pixels: the adjoint of the upsampling, normalized so
/// that a uniform error is corrected in one step.
/// </description></item>
/// <item><description>
/// Luma: with the new chroma, the error of every visible pixel is projected on Y' (the luma row of the matrix, whose
/// coefficients sum to the inverse of the luma scale of the decoder): a gray error is corrected exactly, and a colored
/// error left by the subsampled chroma is corrected in luminance where the chroma cannot follow an edge.
/// </description></item>
/// </list>
/// <para>
/// The planes are clamped to 0 to 255 after each step and rounded once at the end. The refinement runs on the planes the
/// encoder flattened under fully transparent pixels, which neither contribute nor change: their luma, and the chroma
/// samples whose 2x2 pixels are all transparent, keep the flattened values, so that the visible pixels next to them are
/// corrected against the chroma that is encoded. The design is the library's own: a
/// gradient-style correction of the reconstruction error, not a port. Memory: 10 bytes per pixel
/// (<see cref="AllocationKind.Temporary"/>), released before compression.
/// </para>
/// </remarks>
internal static class WebPSharpYuv
{
    /// <summary>The number of chroma-then-luma correction passes.</summary>
    public const int Passes = 4;

    private const float LumaScale = WebPYuvConverter.LumaScale / 65536f;
    private const float RedFromCr = WebPYuvConverter.RedFromCr / 65536f;
    private const float GreenFromCb = WebPYuvConverter.GreenFromCb / 65536f;
    private const float GreenFromCr = WebPYuvConverter.GreenFromCr / 65536f;
    private const float BlueFromCb = WebPYuvConverter.BlueFromCb / 65536f;

    // The forward BT.601 limited-range matrix of the encoder (Vp8EncoderKernels.ConvertLumaRow, Vp8Encoder.ConvertChroma)
    private const float LumaFromRed = 16829 / 65536f;
    private const float LumaFromGreen = 33039 / 65536f;
    private const float LumaFromBlue = 6416 / 65536f;
    private const float CbFromRed = -9714 / 65536f;
    private const float CbFromGreen = -19070 / 65536f;
    private const float CbFromBlue = 28784 / 65536f;
    private const float CrFromRed = 28784 / 65536f;
    private const float CrFromGreen = -24103 / 65536f;
    private const float CrFromBlue = -4681 / 65536f;

    /// <summary>Refines the planes in place.</summary>
    /// <param name="scope">The scope charged for the working memory.</param>
    /// <param name="rgba">The source pixels (<c>width * height * 4</c> bytes).</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="luma">The luma plane (the visible <paramref name="width"/> x <paramref name="height"/> area is read and written).</param>
    /// <param name="lumaStride">The luma row length.</param>
    /// <param name="cb">The Cb plane (the visible <c>(width + 1) / 2</c> x <c>(height + 1) / 2</c> area).</param>
    /// <param name="cr">The Cr plane.</param>
    /// <param name="chromaStride">The chroma row length.</param>
    /// <param name="cancellationToken">Checked between rows.</param>
    public static void Refine(AllocationScope scope, ReadOnlySpan<byte> rgba, int width, int height, Span<byte> luma, int lumaStride, Span<byte> cb, Span<byte> cr, int chromaStride, CancellationToken cancellationToken)
    {
        var chromaWidth = (width + 1) >> 1;
        var chromaHeight = (height + 1) >> 1;
        var pixels = (long)width * height;
        var samples = (long)chromaWidth * chromaHeight;
        var floats = pixels + (5 * samples);
        if (floats * sizeof(float) > CheckedSizes.MaxBufferLength)
            throw CheckedSizes.CreateOverflowException(scope.Limits);

        using var buffer = scope.Rent((int)(floats * sizeof(float)), AllocationKind.Temporary, clear: false);
        var all = unsafe(MemoryMarshal.Cast<byte, float>(buffer.RawBuffer.AsSpan(0, (int)(floats * sizeof(float)))));
        var y = all[..(int)pixels];
        var u = all.Slice((int)pixels, (int)samples);
        var v = all.Slice((int)(pixels + samples), (int)samples);
        var sumU = all.Slice((int)(pixels + (2 * samples)), (int)samples);
        var sumV = all.Slice((int)(pixels + (3 * samples)), (int)samples);
        var weights = all.Slice((int)(pixels + (4 * samples)), (int)samples);

        for (var row = 0; row < height; row++)
        {
            for (var x = 0; x < width; x++)
            {
                y[(row * width) + x] = luma[(row * lumaStride) + x];
            }
        }

        for (var row = 0; row < chromaHeight; row++)
        {
            for (var x = 0; x < chromaWidth; x++)
            {
                u[(row * chromaWidth) + x] = cb[(row * chromaStride) + x];
                v[(row * chromaWidth) + x] = cr[(row * chromaStride) + x];
            }
        }

        var context = new Planes(rgba, width, height, chromaWidth, chromaHeight, y, u, v);
        for (var pass = 0; pass < Passes; pass++)
        {
            CorrectChroma(context, sumU, sumV, weights, cancellationToken);
            CorrectLuma(context, cancellationToken);
        }

        for (var row = 0; row < height; row++)
        {
            for (var x = 0; x < width; x++)
            {
                if (rgba[(((row * width) + x) * 4) + 3] != 0)
                {
                    luma[(row * lumaStride) + x] = Round(y[(row * width) + x]);
                }
            }
        }

        for (var row = 0; row < chromaHeight; row++)
        {
            for (var x = 0; x < chromaWidth; x++)
            {
                cb[(row * chromaStride) + x] = Round(u[(row * chromaWidth) + x]);
                cr[(row * chromaStride) + x] = Round(v[(row * chromaWidth) + x]);
            }
        }
    }

    private static void CorrectChroma(Planes planes, Span<float> sumU, Span<float> sumV, Span<float> weights, CancellationToken cancellationToken)
    {
        sumU.Clear();
        sumV.Clear();
        weights.Clear();
        for (var row = 0; row < planes.Height; row++)
        {
            if ((row & 63) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var nearRow = row >> 1;
            var farRow = (row & 1) == 0 ? Math.Max(nearRow - 1, 0) : Math.Min(nearRow + 1, planes.ChromaHeight - 1);
            for (var x = 0; x < planes.Width; x++)
            {
                int alpha = planes.Rgba[(((row * planes.Width) + x) * 4) + 3];
                if (alpha == 0)
                    continue;

                var (er, eg, eb) = planes.GetError(row, x, nearRow, farRow);
                var du = (CbFromRed * er) + (CbFromGreen * eg) + (CbFromBlue * eb);
                var dv = (CrFromRed * er) + (CrFromGreen * eg) + (CrFromBlue * eb);
                var nearColumn = x >> 1;
                var farColumn = (x & 1) == 0 ? Math.Max(nearColumn - 1, 0) : Math.Min(nearColumn + 1, planes.ChromaWidth - 1);
                Accumulate(sumU, sumV, weights, (nearRow * planes.ChromaWidth) + nearColumn, 9 * alpha, du, dv);
                Accumulate(sumU, sumV, weights, (nearRow * planes.ChromaWidth) + farColumn, 3 * alpha, du, dv);
                Accumulate(sumU, sumV, weights, (farRow * planes.ChromaWidth) + nearColumn, 3 * alpha, du, dv);
                Accumulate(sumU, sumV, weights, (farRow * planes.ChromaWidth) + farColumn, alpha, du, dv);
            }
        }

        for (var i = 0; i < weights.Length; i++)
        {
            if (weights[i] > 0 && HasVisiblePixel(planes, i))
            {
                planes.U[i] = Math.Clamp(planes.U[i] + (sumU[i] / weights[i]), 0, 255);
                planes.V[i] = Math.Clamp(planes.V[i] + (sumV[i] / weights[i]), 0, 255);
            }
        }
    }

    /// <summary>Gets whether the 2x2 block of chroma sample <paramref name="index"/> has a pixel that is not fully transparent.</summary>
    private static bool HasVisiblePixel(Planes planes, int index)
    {
        var cx = index % planes.ChromaWidth;
        var cy = index / planes.ChromaWidth;
        for (var y = 2 * cy; y < Math.Min((2 * cy) + 2, planes.Height); y++)
        {
            for (var x = 2 * cx; x < Math.Min((2 * cx) + 2, planes.Width); x++)
            {
                if (planes.Rgba[(((y * planes.Width) + x) * 4) + 3] != 0)
                    return true;
            }
        }

        return false;
    }

    private static void Accumulate(Span<float> sumU, Span<float> sumV, Span<float> weights, int index, float weight, float du, float dv)
    {
        sumU[index] += weight * du;
        sumV[index] += weight * dv;
        weights[index] += weight;
    }

    private static void CorrectLuma(Planes planes, CancellationToken cancellationToken)
    {
        for (var row = 0; row < planes.Height; row++)
        {
            if ((row & 63) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var nearRow = row >> 1;
            var farRow = (row & 1) == 0 ? Math.Max(nearRow - 1, 0) : Math.Min(nearRow + 1, planes.ChromaHeight - 1);
            for (var x = 0; x < planes.Width; x++)
            {
                if (planes.Rgba[(((row * planes.Width) + x) * 4) + 3] == 0)
                    continue;

                var (er, eg, eb) = planes.GetError(row, x, nearRow, farRow);
                var index = (row * planes.Width) + x;
                planes.Y[index] = Math.Clamp(planes.Y[index] + (LumaFromRed * er) + (LumaFromGreen * eg) + (LumaFromBlue * eb), 0, 255);
            }
        }
    }

    private static byte Round(float value) => (byte)Math.Clamp((int)MathF.Floor(value + 0.5f), 0, 255);

    private readonly ref struct Planes(ReadOnlySpan<byte> rgba, int width, int height, int chromaWidth, int chromaHeight, Span<float> y, Span<float> u, Span<float> v)
    {
        public ReadOnlySpan<byte> Rgba { get; } = rgba;

        public int Width { get; } = width;

        public int Height { get; } = height;

        public int ChromaWidth { get; } = chromaWidth;

        public int ChromaHeight { get; } = chromaHeight;

        public Span<float> Y { get; } = y;

        public Span<float> U { get; } = u;

        public Span<float> V { get; } = v;

        /// <summary>Gets the source minus the reconstruction of a pixel, with the upsampling and the conversion of the decoder.</summary>
        public (float Red, float Green, float Blue) GetError(int row, int x, int nearRow, int farRow)
        {
            var nearColumn = x >> 1;
            var farColumn = (x & 1) == 0 ? Math.Max(nearColumn - 1, 0) : Math.Min(nearColumn + 1, ChromaWidth - 1);
            var near = nearRow * ChromaWidth;
            var far = farRow * ChromaWidth;
            var cb = ((9 * U[near + nearColumn]) + (3 * U[near + farColumn]) + (3 * U[far + nearColumn]) + U[far + farColumn]) / 16;
            var cr = ((9 * V[near + nearColumn]) + (3 * V[near + farColumn]) + (3 * V[far + nearColumn]) + V[far + farColumn]) / 16;
            var c = LumaScale * (Y[(row * Width) + x] - 16);
            var red = Math.Clamp(c + (RedFromCr * (cr - 128)), 0, 255);
            var green = Math.Clamp(c - (GreenFromCb * (cb - 128)) - (GreenFromCr * (cr - 128)), 0, 255);
            var blue = Math.Clamp(c + (BlueFromCb * (cb - 128)), 0, 255);
            var pixel = Rgba.Slice(((row * Width) + x) * 4, 3);
            return (pixel[0] - red, pixel[1] - green, pixel[2] - blue);
        }
    }
}
