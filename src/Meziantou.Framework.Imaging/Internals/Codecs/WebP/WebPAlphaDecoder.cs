using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Decodes an <c>ALPH</c> chunk (WebP container specification, "Alpha"): the header byte (reserved, preprocessing, filtering
/// method, compression method), raw or lossless-compressed alpha values (the green component of a headerless VP8L image
/// stream) and the inverse of the horizontal, vertical or gradient filter.
/// </summary>
/// <remarks>
/// The reserved bits and the informative preprocessing bits are ignored (the container specification says readers must
/// ignore reserved fields; preprocessing only describes what the encoder did). Compression methods 2 and 3 and raw data
/// shorter than <c>width * height</c> are <see cref="InvalidImageContentException"/>; raw data beyond it is ignored.
/// </remarks>
internal static class WebPAlphaDecoder
{
    /// <summary>Decodes the alpha plane of a <paramref name="width"/> x <paramref name="height"/> frame.</summary>
    /// <param name="scope">The scope charged for the plane and the decoder state.</param>
    /// <param name="data">The buffer holding the chunk payload.</param>
    /// <param name="offset">The offset of the payload (its header byte).</param>
    /// <param name="length">The payload length.</param>
    /// <param name="width">The frame width.</param>
    /// <param name="height">The frame height.</param>
    /// <param name="cancellationToken">Checked between rows of the lossless image stream.</param>
    /// <returns><c>width * height</c> alpha values in scan order; the caller disposes them.</returns>
    public static PooledBuffer Decode(AllocationScope scope, byte[] data, int offset, int length, int width, int height, CancellationToken cancellationToken)
    {
        if (length < 1)
            throw Invalid("The WebP alpha (ALPH) chunk is empty.");

        var header = data[offset];
        var compression = header & 0x03;
        var filter = (header >> 2) & 0x03;
        if (compression > 1)
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The WebP alpha compression method {compression} is not defined (0 = none, 1 = lossless)."));

        var count = (long)width * height;
        if (count > CheckedSizes.MaxBufferLength)
            throw CheckedSizes.CreateOverflowException(scope.Limits);

        var plane = scope.Rent((int)count, AllocationKind.DecoderState, clear: false);
        try
        {
            var alpha = plane.RawBuffer.AsSpan(0, (int)count);
            if (compression == 0)
            {
                if (length - 1 < count)
                    throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The uncompressed WebP alpha data has {length - 1} bytes; {count} are needed."));

                data.AsSpan(offset + 1, (int)count).CopyTo(alpha);
            }
            else
            {
                using var argb = Vp8LDecoder.DecodeImageStream(scope, data, offset + 1, length - 1, width, height, cancellationToken);
                var pixels = unsafe(MemoryMarshal.Cast<byte, uint>(argb.RawBuffer.AsSpan()))[..(int)count];
                ExtractGreen(pixels, alpha);
            }

            Unfilter(alpha, width, height, filter);
            return plane;
        }
        catch
        {
            plane.Dispose();
            throw;
        }
    }

    /// <summary>Copies the green component (the alpha values) of ARGB pixels, 16 at a time.</summary>
    internal static void ExtractGreen(ReadOnlySpan<uint> pixels, Span<byte> alpha)
    {
        var i = 0;
        if (Vector128.IsHardwareAccelerated)
        {
            for (; i <= alpha.Length - Vector128<byte>.Count; i += Vector128<byte>.Count)
            {
                var low = Vector128.Narrow(Green(pixels, i), Green(pixels, i + 4));
                var high = Vector128.Narrow(Green(pixels, i + 8), Green(pixels, i + 12));
                Vector128.Narrow(low, high).CopyTo(alpha.Slice(i, Vector128<byte>.Count));
            }
        }

        for (; i < alpha.Length; i++)
        {
            alpha[i] = (byte)(pixels[i] >> 8);
        }

        // The narrowing keeps the low half of each lane: the green byte once it is shifted down, the high bytes masked away
        static Vector128<uint> Green(ReadOnlySpan<uint> pixels, int start) => Vector128.ShiftRightLogical(Vector128.Create(pixels.Slice(start, Vector128<uint>.Count)), 8) & Vector128.Create(0xFFu);
    }

    /// <summary>Reverses an alpha filter in place: <c>alpha = (predictor + value) % 256</c>.</summary>
    /// <param name="alpha">The filtered values, in scan order.</param>
    /// <param name="width">The frame width.</param>
    /// <param name="height">The frame height.</param>
    /// <param name="filter">0 none, 1 horizontal, 2 vertical, 3 gradient.</param>
    /// <remarks>Each filter has its own row loop; the vertical filter adds the row above 16 values at a time.</remarks>
    public static void Unfilter(Span<byte> alpha, int width, int height, int filter)
    {
        if (filter == 0 || alpha.IsEmpty)
            return;

        // First row: the left neighbor (0 for the first value), whatever the filter
        var first = alpha[..width];
        for (var x = 1; x < width; x++)
        {
            first[x] += first[x - 1];
        }

        for (var y = 1; y < height; y++)
        {
            var row = alpha.Slice(y * width, width);
            var above = alpha.Slice((y - 1) * width, width);

            // First column: the top neighbor, whatever the filter
            row[0] += above[0];
            switch (filter)
            {
                case 1:
                {
                    var left = row[0];
                    for (var x = 1; x < width; x++)
                    {
                        left += row[x];
                        row[x] = left;
                    }

                    break;
                }

                case 2:
                {
                    var x = 1;
                    if (Vector128.IsHardwareAccelerated)
                    {
                        for (; x <= width - Vector128<byte>.Count; x += Vector128<byte>.Count)
                        {
                            var target = row.Slice(x, Vector128<byte>.Count);
                            (Vector128.Create<byte>(target) + Vector128.Create<byte>(above.Slice(x, Vector128<byte>.Count))).CopyTo(target);
                        }
                    }

                    for (; x < width; x++)
                    {
                        row[x] += above[x];
                    }

                    break;
                }

                default:
                {
                    int left = row[0];
                    for (var x = 1; x < width; x++)
                    {
                        left = (byte)(row[x] + Math.Clamp(left + above[x] - above[x - 1], 0, 255));
                        row[x] = (byte)left;
                    }

                    break;
                }
            }
        }
    }

    /// <summary>The scalar reference of <see cref="Unfilter"/>: the prediction of every value through <see cref="Predict"/>.</summary>
    internal static void UnfilterScalar(Span<byte> alpha, int width, int height, int filter)
    {
        if (filter == 0)
            return;

        for (var y = 0; y < height; y++)
        {
            var row = alpha.Slice(y * width, width);
            var above = y == 0 ? default : alpha.Slice((y - 1) * width, width);
            for (var x = 0; x < width; x++)
            {
                row[x] = (byte)(row[x] + Predict(row, above, x, y, filter));
            }
        }
    }

    /// <summary>
    /// The filter prediction of the value at (<paramref name="x"/>, <paramref name="y"/>) from the reconstructed values: the
    /// top-left value predicts 0, the rest of the first row its left neighbor and the rest of the first column its top
    /// neighbor (horizontal and gradient filters use the top neighbor on the first column, vertical and gradient filters the
    /// left neighbor on the first row).
    /// </summary>
    public static int Predict(ReadOnlySpan<byte> row, ReadOnlySpan<byte> above, int x, int y, int filter)
    {
        if (y == 0)
            return x == 0 ? 0 : row[x - 1];

        if (x == 0)
            return above[0];

        return filter switch
        {
            1 => row[x - 1],
            2 => above[x],
            _ => Math.Clamp(row[x - 1] + above[x] - above[x - 1], 0, 255),
        };
    }

    private static InvalidImageContentException Invalid(string message) => new(message, ImageFormat.WebP);
}
