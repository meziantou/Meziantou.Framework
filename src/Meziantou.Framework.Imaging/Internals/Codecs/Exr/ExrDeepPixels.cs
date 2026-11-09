using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The pixels of a deep chunk (compression notes, section 1.6) and their flattening (section 1.7): the samples of a pixel
/// are composited in their stored order with the <em>over</em> operation, in single precision, until the accumulated alpha
/// reaches 1. The samples of one source are not sorted: OpenEXR sorts the samples of a pixel only when they come from
/// several sources.
/// </summary>
internal static class ExrDeepPixels
{
    /// <summary>Gets the size of the uncompressed sample count table of a chunk: one 4-byte entry per pixel of the (clipped) chunk.</summary>
    public static int GetTableLength(int width, int rows) => checked(width * rows * 4);

    /// <summary>
    /// Checks the sample count table (cumulative within each row, never decreasing) and gives the number of samples of
    /// each row.
    /// </summary>
    /// <returns>The size of the samples the table describes.</returns>
    public static long GetRowTotals(ReadOnlySpan<int> counts, int width, int rows, IReadOnlyList<ExrChannel> channels, Span<int> rowTotals)
    {
        long bytesPerSample = 0;
        foreach (var channel in channels)
        {
            bytesPerSample += channel.BytesPerSample;
        }

        long total = 0;
        for (var row = 0; row < rows; row++)
        {
            var previous = 0;
            var entries = counts.Slice(row * width, width);
            foreach (var entry in entries)
            {
                if (entry < previous)
                    throw ExrFormat.Invalid("the sample count table of a deep chunk decreases.");

                previous = entry;
            }

            rowTotals[row] = previous;
            total += previous;
        }

        return total * bytesPerSample;
    }

    /// <summary>Flattens the pixels of a chunk that are inside the image window and writes them as <see cref="RgbaFloat"/>.</summary>
    public static void Flatten(ExrHeader header, PixelStorage target, ReadOnlySpan<int> counts, int width, int rows, ReadOnlySpan<int> rowTotals, ReadOnlySpan<byte> samples, int x0, int y0)
    {
        var image = header.ImageWindow;
        var first = Math.Max(x0, image.MinX);
        var last = Math.Min((long)x0 + width - 1, image.MaxX);
        var channels = header.Channels;
        var roles = header.Roles;
        Span<int> channelOffsets = channels.Count <= 64 ? stackalloc int[channels.Count] : new int[channels.Count];
        Span<float> output = stackalloc float[5];
        var rowOffset = 0;
        for (var row = 0; row < rows; row++)
        {
            var total = rowTotals[row];
            var offset = rowOffset;
            for (var c = 0; c < channels.Count; c++)
            {
                channelOffsets[c] = offset;
                offset += total * channels[c].BytesPerSample;
            }

            rowOffset = offset;
            var y = (long)y0 + row;
            if (y < image.MinY || y > image.MaxY || first > last)
                continue;

            var pixels = target.GetRowSpanCore((int)(y - image.MinY));
            for (var x = first; x <= last; x++)
            {
                var column = (int)(x - x0);
                var end = counts[(row * width) + column];
                var start = column == 0 ? 0 : counts[(row * width) + column - 1];
                Composite(channels, roles, samples, channelOffsets, start, end, output);
                var pixel = pixels.Slice((int)(x - image.MinX) * 16, 16);
                var isLuminance = header.IsLuminance;
                for (var component = 0; component < 3; component++)
                {
                    BinaryPrimitives.WriteSingleLittleEndian(pixel[(component * 4)..], isLuminance ? output[(int)ExrChannelRole.Luminance] : output[component]);
                }

                BinaryPrimitives.WriteSingleLittleEndian(pixel[12..], output[(int)ExrChannelRole.Alpha]);
                ExrDecoder.Unpremultiply(pixel, 1);
            }
        }
    }

    /// <summary>
    /// Composites the samples <paramref name="start"/> to <paramref name="end"/> - 1 of a pixel into
    /// <paramref name="output"/> (indexed by <see cref="ExrChannelRole"/>): each sample adds (1 - alpha) times its values,
    /// alpha being the accumulated alpha before it, each operation rounded to single precision.
    /// </summary>
    private static void Composite(IReadOnlyList<ExrChannel> channels, ExrChannelRole[] roles, ReadOnlySpan<byte> samples, ReadOnlySpan<int> channelOffsets, int start, int end, Span<float> output)
    {
        output.Clear();
        for (var s = start; s < end; s++)
        {
            var alpha = output[(int)ExrChannelRole.Alpha];
            if (alpha >= 1f)
                break;

            var weight = 1f - alpha;
            for (var c = 0; c < channels.Count; c++)
            {
                var role = roles[c];
                if (role > ExrChannelRole.Luminance)
                    continue;

                var channel = channels[c];
                var position = channelOffsets[c] + (s * channel.BytesPerSample);
                var value = channel.PixelType switch
                {
                    ExrPixelType.Half => (float)BinaryPrimitives.ReadHalfLittleEndian(samples[position..]),
                    ExrPixelType.Float => BinaryPrimitives.ReadSingleLittleEndian(samples[position..]),
                    _ => (float)BinaryPrimitives.ReadUInt32LittleEndian(samples[position..]),
                };

                var product = weight * value;
                output[(int)role] += product;
            }
        }
    }
}
