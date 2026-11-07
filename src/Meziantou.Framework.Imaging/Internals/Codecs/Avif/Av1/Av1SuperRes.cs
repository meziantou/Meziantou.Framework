namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The super-resolution upscaling of AV1 (AV1 specification, section 7.16): a horizontal upscaling of every row of a frame.</summary>
internal static class Av1SuperRes
{
    /// <summary>
    /// Upscales a row of a plane from <paramref name="downscaledWidth"/> to <paramref name="upscaledWidth"/> samples.
    /// </summary>
    /// <param name="source">The row before upscaling: at least <paramref name="maxX"/> + 1 samples.</param>
    /// <param name="destination">The upscaled row.</param>
    /// <param name="downscaledWidth"><c>downscaledPlaneW</c>.</param>
    /// <param name="upscaledWidth"><c>upscaledPlaneW</c>.</param>
    /// <param name="maxX">The last valid sample of the row before upscaling (<c>miW * MI_SIZE - 1</c>).</param>
    /// <param name="bitDepth">The bit depth of the samples.</param>
    public static void UpscaleRow(ReadOnlySpan<ushort> source, Span<ushort> destination, int downscaledWidth, int upscaledWidth, int maxX, int bitDepth)
    {
        var stepX = ((downscaledWidth << Av1Constants.SuperresScaleBits) + (upscaledWidth / 2)) / upscaledWidth;
        var err = (upscaledWidth * stepX) - (downscaledWidth << Av1Constants.SuperresScaleBits);
        var initialSubpelX = ((-((upscaledWidth - downscaledWidth) << (Av1Constants.SuperresScaleBits - 1)) + (upscaledWidth / 2)) / upscaledWidth)
            + (1 << (Av1Constants.SuperresExtraBits - 1)) - (err / 2);
        initialSubpelX &= Av1Constants.SuperresScaleMask;
        var maxSample = (1 << bitDepth) - 1;
        var filters = Av1Tables.UpscaleFilter;
        for (var x = 0; x < upscaledWidth; x++)
        {
            var srcX = -(1 << Av1Constants.SuperresScaleBits) + initialSubpelX + (x * stepX);
            var srcXPx = srcX >> Av1Constants.SuperresScaleBits;
            var srcXSubpel = (srcX & Av1Constants.SuperresScaleMask) >> Av1Constants.SuperresExtraBits;
            var filter = filters.Slice(srcXSubpel * Av1Constants.SuperresFilterTaps, Av1Constants.SuperresFilterTaps);
            var sum = 0;
            for (var k = 0; k < Av1Constants.SuperresFilterTaps; k++)
            {
                var sampleX = Math.Clamp(srcXPx + (k - Av1Constants.SuperresFilterOffset), 0, maxX);
                sum += source[sampleX] * filter[k];
            }

            destination[x] = (ushort)Math.Clamp((sum + (1 << (Av1Constants.FilterBits - 1))) >> Av1Constants.FilterBits, 0, maxSample);
        }
    }
}
