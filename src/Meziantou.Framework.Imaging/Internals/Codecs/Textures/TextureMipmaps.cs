using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Generates the mipmap levels of a texture: each level halves the dimensions of the previous one (rounded down, at least
/// 1), and each of its texels is the average of the texels of the previous level it covers.
/// </summary>
/// <remarks>
/// <para>
/// The filter is a box over the exact area a texel covers: a level of an odd size takes a third of the texel in the middle
/// of each group of three, so no texel of the previous level is skipped or counted twice. The colors are weighted by their
/// alpha, so that transparent texels do not bleed their hidden colors; a texel whose texels are all transparent keeps the
/// unweighted average of their colors and an alpha of 0.
/// </para>
/// <para>
/// sRGB-encoded integer samples are averaged in linear light, then encoded again to the nearest code: the sRGB transfer
/// function is evaluated with the rational approximation of <see cref="LinearRgbLoader.ToLinear(float)"/>, which uses fused
/// multiply-adds only (no <c>Math.Pow</c>), and its inverse by a search over the codes, so the levels are the same on every
/// platform. Floating-point samples, and integer samples labeled linear, are averaged as they are.
/// </para>
/// </remarks>
internal static class TextureMipmaps
{
    private static readonly float[] ByteToLinear = CreateByteTable();

    /// <summary>Gets the number of levels of a full chain, down to 1×1.</summary>
    public static int GetLevelCount(int width, int height)
    {
        var size = Math.Max(width, height);
        var count = 1;
        while (size > 1)
        {
            size >>= 1;
            count++;
        }

        return count;
    }

    /// <summary>Gets the size of a level of a surface.</summary>
    public static int GetLevelSize(int size, int level) => Math.Max(1, size >> level);

    /// <summary>Creates the next level of a surface.</summary>
    /// <param name="source">The level.</param>
    /// <param name="srgb">Whether the integer samples are sRGB-encoded, and averaged in linear light.</param>
    /// <param name="scope">The scope charged for the new level and the rows of the filter.</param>
    /// <param name="cancellationToken">Checked between rows.</param>
    /// <returns>The next level, owned by the caller.</returns>
    public static TexturePixels Downsample(TexturePixels source, bool srgb, AllocationScope scope, CancellationToken cancellationToken)
    {
        var width = Math.Max(1, source.Width / 2);
        var height = Math.Max(1, source.Height / 2);
        var linear = srgb && source.Precision != TexturePrecision.Float;
        var destination = TexturePixels.Create(width, height, source.Precision, scope);
        try
        {
            // The accumulated premultiplied colors and alpha of a destination row, and the unweighted colors
            var accumulatorLength = (long)width * 8 * sizeof(double);
            if (accumulatorLength > CheckedSizes.MaxBufferLength)
                throw CheckedSizes.CreateOverflowException(scope.Limits);

            using var accumulatorBuffer = scope.Rent((int)accumulatorLength, AllocationKind.Temporary, clear: false);
            var accumulator = unsafe(MemoryMarshal.Cast<byte, double>(accumulatorBuffer.Span));
            Span<float> pixel = stackalloc float[4];
            for (var y = 0; y < height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                accumulator.Clear();

                // The rows of the source the destination row covers: [y × H, (y + 1) × H) in units of 1 / height
                var top = (long)y * source.Height;
                var bottom = top + source.Height;
                for (var sy = (int)(top / height); sy < source.Height && (long)sy * height < bottom; sy++)
                {
                    var rowWeight = Overlap((long)sy * height, ((long)sy + 1) * height, top, bottom);
                    for (var x = 0; x < width; x++)
                    {
                        var left = (long)x * source.Width;
                        var right = left + source.Width;
                        for (var sx = (int)(left / width); sx < source.Width && (long)sx * width < right; sx++)
                        {
                            var weight = rowWeight * Overlap((long)sx * width, ((long)sx + 1) * width, left, right);
                            source.GetPixel(sx, sy, pixel);
                            var alpha = (double)pixel[3];
                            var slot = x * 8;
                            for (var c = 0; c < 3; c++)
                            {
                                var value = linear ? ToLinear(source, sx, sy, c, pixel[c]) : pixel[c];
                                accumulator[slot + c] += weight * alpha * value;
                                accumulator[slot + 4 + c] += weight * value;
                            }

                            accumulator[slot + 3] += weight * alpha;
                        }
                    }
                }

                // Every weight is a product of two overlaps: their total is width × height of the source per texel
                var total = (double)source.Width * source.Height;
                for (var x = 0; x < width; x++)
                {
                    var slot = x * 8;
                    var alpha = accumulator[slot + 3] / total;
                    for (var c = 0; c < 3; c++)
                    {
                        var value = accumulator[slot + 3] > 0 ? accumulator[slot + c] / accumulator[slot + 3] : accumulator[slot + 4 + c] / total;
                        Store(destination, x, y, c, value, linear);
                    }

                    Store(destination, x, y, 3, alpha, linear: false);
                }
            }

            return destination;
        }
        catch
        {
            destination.Dispose();
            throw;
        }
    }

    private static long Overlap(long start, long end, long otherStart, long otherEnd) => Math.Max(0, Math.Min(end, otherEnd) - Math.Max(start, otherStart));

    private static double ToLinear(TexturePixels source, int x, int y, int channel, float value) => source.Precision == TexturePrecision.Byte
        ? ByteToLinear[source.GetBytes(y)[(x * 4) + channel]]
        : LinearRgbLoader.ToLinear(value);

    private static void Store(TexturePixels destination, int x, int y, int channel, double value, bool linear)
    {
        switch (destination.Precision)
        {
            case TexturePrecision.Byte:
                destination.GetBytes(y)[(x * 4) + channel] = (byte)(linear ? EncodeByte(value) : Quantize(value, 255));
                break;

            case TexturePrecision.UInt16:
                destination.GetWords(y)[(x * 4) + channel] = (ushort)(linear ? EncodeWord(value) : Quantize(value, 65535));
                break;

            default:
                destination.GetFloats(y)[(x * 4) + channel] = (float)value;
                break;
        }
    }

    private static int Quantize(double value, int max) => value >= 1 ? max : value <= 0 ? 0 : (int)((value * max) + 0.5);

    /// <summary>Gets the 8-bit code whose linear value is nearest: the codes are increasing, so a binary search over the midpoints.</summary>
    private static int EncodeByte(double value)
    {
        var low = 0;
        var high = 255;
        while (low < high)
        {
            var middle = (low + high) >> 1;
            if (value > (ByteToLinear[middle] + (double)ByteToLinear[middle + 1]) / 2)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    /// <summary>Gets the 16-bit code whose linear value is nearest, by a binary search over the codes.</summary>
    private static int EncodeWord(double value)
    {
        var low = 0;
        var high = 65535;
        while (low < high)
        {
            var middle = (low + high) >> 1;
            var midpoint = (LinearRgbLoader.ToLinear(middle / 65535f) + (double)LinearRgbLoader.ToLinear((middle + 1) / 65535f)) / 2;
            if (value > midpoint)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    private static float[] CreateByteTable()
    {
        var table = new float[256];
        for (var i = 0; i < table.Length; i++)
        {
            table[i] = LinearRgbLoader.ToLinear(i / 255f);
        }

        return table;
    }
}
