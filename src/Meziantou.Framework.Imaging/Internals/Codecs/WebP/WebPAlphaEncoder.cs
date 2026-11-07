using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Encodes the alpha values of a lossy frame as an <c>ALPH</c> chunk payload (WebP container specification, "Alpha"):
/// lossless compression (a headerless VP8L image stream whose green component holds the filtered values). The filter (none,
/// horizontal, vertical or gradient) is chosen by size: from effort 3, every filter is compressed at effort 0 and the
/// smallest one is compressed at the requested effort; at effort 9, every filter is compressed at effort 9 and the smallest
/// stream is kept. Below effort 3, no filter. Alpha is exact unless an alpha quality below 100 reduced its levels first
/// (<see cref="QuantizeLevels"/>, flagged as preprocessing in the header).
/// </summary>
/// <remarks>
/// The entropy of the filtered values is a poor guide: the VP8L stream applies its own spatial prediction and backward
/// references to them, so the filter whose values look most random can compress best (a soft mask: horizontal differences
/// that repeat from row to row compress to half the size of the smaller gradient residuals).
/// </remarks>
internal static class WebPAlphaEncoder
{
    // The filtered values are computed in chunks of a row
    private const int ChunkLength = 256;

    /// <summary>Encodes the alpha channel of <paramref name="rgba"/>.</summary>
    /// <returns>The compressed bitstream (after the header byte) and the header byte.</returns>
    /// <param name="scope">The scope charged for the working memory.</param>
    /// <param name="rgba">The pixels, whose alpha values are already reduced when <paramref name="levelsReduced"/> is set.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="effort">The effort, 0 to 9.</param>
    /// <param name="levelsReduced">Whether <see cref="QuantizeLevels"/> reduced the alpha values (the preprocessing flag of the header).</param>
    /// <param name="cancellationToken">Checked by the lossless encoder.</param>
    public static (LsbBitWriter Bitstream, byte Header) Encode(AllocationScope scope, ReadOnlySpan<byte> rgba, int width, int height, int effort, bool levelsReduced, CancellationToken cancellationToken)
    {
        var count = width * height;
        using var alphaBuffer = scope.Rent(count, AllocationKind.Temporary, clear: false);
        var alpha = alphaBuffer.RawBuffer.AsSpan(0, count);
        for (var i = 0; i < count; i++)
        {
            alpha[i] = rgba[(i * 4) + 3];
        }

        using var pixelBuffer = scope.Rent(count * sizeof(uint), AllocationKind.Temporary, clear: false);
        var argb = unsafe(System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(pixelBuffer.RawBuffer.AsSpan()))[..count];
        if (effort < 3)
            return (EncodeFiltered(scope, alpha, argb, width, height, filter: 0, effort, cancellationToken), Header(0, levelsReduced));

        // Effort 9 keeps the smallest full stream; lower efforts choose the filter of the smallest effort-0 stream
        var trialEffort = effort >= 9 ? effort : 0;
        LsbBitWriter? best = null;
        var bestFilter = 0;
        try
        {
            for (var filter = 0; filter < 4; filter++)
            {
                var stream = EncodeFiltered(scope, alpha, argb, width, height, filter, trialEffort, cancellationToken);
                if (best is null || stream.Length < best.Length)
                {
                    best?.Dispose();
                    best = stream;
                    bestFilter = filter;
                }
                else
                {
                    stream.Dispose();
                }
            }

            if (trialEffort != effort)
            {
                best!.Dispose();
                best = null;
                best = EncodeFiltered(scope, alpha, argb, width, height, bestFilter, effort, cancellationToken);
            }

            var result = (best!, Header(bestFilter, levelsReduced));
            best = null;
            return result;
        }
        finally
        {
            best?.Dispose();
        }

        // Lossless compression (1), the filter and the preprocessing (1: level reduction)
        static byte Header(int filter, bool levelsReduced) => (byte)(1 | (filter << 2) | (levelsReduced ? 1 << 4 : 0));
    }

    /// <summary>Gets the number of alpha levels of an alpha quality: 256 (exact) at 100, down to 2 at 0, as <c>cwebp -alpha_q</c>.</summary>
    public static int GetLevelCount(int alphaQuality) => alphaQuality <= 70 ? 2 + (alphaQuality / 5) : 16 + ((alphaQuality - 70) * 8);

    /// <summary>
    /// Reduces the alpha values of <paramref name="rgba"/> to at most <see cref="GetLevelCount"/> levels in place, with the
    /// levels of smallest squared error (Lloyd's algorithm on the histogram). The smallest and the largest value are always
    /// levels, so fully transparent and fully opaque pixels stay exact.
    /// </summary>
    /// <returns>Whether any value changed.</returns>
    public static bool QuantizeLevels(Span<byte> rgba, int alphaQuality)
    {
        var levelCount = GetLevelCount(alphaQuality);
        Span<long> histogram = stackalloc long[256];
        histogram.Clear();
        for (var i = 3; i < rgba.Length; i += 4)
        {
            histogram[rgba[i]]++;
        }

        var used = 0;
        int min = 255, max = 0;
        for (var value = 0; value < 256; value++)
        {
            if (histogram[value] == 0)
                continue;

            used++;
            min = Math.Min(min, value);
            max = Math.Max(max, value);
        }

        if (used <= levelCount)
            return false;

        // Lloyd's algorithm: levels at the mean of their cell, cells split halfway between levels; the ends stay fixed
        Span<double> levels = stackalloc double[levelCount];
        for (var k = 0; k < levelCount; k++)
        {
            levels[k] = min + ((max - min) * (double)k / (levelCount - 1));
        }

        for (var iteration = 0; iteration < 32; iteration++)
        {
            var changed = false;
            var value = min;
            for (var k = 1; k < levelCount - 1; k++)
            {
                var low = (levels[k - 1] + levels[k]) / 2;
                var high = (levels[k] + levels[k + 1]) / 2;
                double sum = 0, weight = 0;
                for (value = Math.Max(value, (int)Math.Ceiling(low)); value < high; value++)
                {
                    sum += value * (double)histogram[value];
                    weight += histogram[value];
                }

                if (weight > 0 && Math.Abs((sum / weight) - levels[k]) > 1e-9)
                {
                    levels[k] = sum / weight;
                    changed = true;
                }
            }

            if (!changed)
                break;
        }

        Span<byte> map = stackalloc byte[256];
        var level = 0;
        for (var value = 0; value < 256; value++)
        {
            while (level < levelCount - 1 && Math.Abs(levels[level + 1] - value) <= Math.Abs(levels[level] - value))
            {
                level++;
            }

            map[value] = (byte)Math.Round(levels[level], MidpointRounding.AwayFromZero);
        }

        var any = false;
        for (var i = 3; i < rgba.Length; i += 4)
        {
            var quantized = map[rgba[i]];
            any |= quantized != rgba[i];
            rgba[i] = quantized;
        }

        return any;
    }

    /// <summary>Compresses the alpha values filtered by <paramref name="filter"/>, using <paramref name="argb"/> as working pixels.</summary>
    private static LsbBitWriter EncodeFiltered(AllocationScope scope, ReadOnlySpan<byte> alpha, Span<uint> argb, int width, int height, int filter, int effort, CancellationToken cancellationToken)
    {
        Span<byte> values = stackalloc byte[ChunkLength];
        for (var y = 0; y < height; y++)
        {
            for (var x0 = 0; x0 < width; x0 += ChunkLength)
            {
                var x1 = Math.Min(width, x0 + ChunkLength);
                var chunk = values[..(x1 - x0)];
                FilterRow(alpha, width, y, x0, x1, filter, chunk);
                ToGreen(chunk, argb.Slice((y * width) + x0, x1 - x0));
            }
        }

        var writer = new LsbBitWriter(scope);
        try
        {
            Vp8LEncoder.EncodeImageStream(scope, writer, argb, width, height, effort, cancellationToken);
            writer.Finish();
            return writer;
        }
        catch
        {
            writer.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Computes the filtered values <paramref name="x0"/> to <paramref name="x1"/> of row <paramref name="y"/>: the value minus
    /// its prediction from the original values (<see cref="WebPAlphaDecoder.Predict"/>), the value itself without filter.
    /// Values that are not on the first row or column are computed 16 at a time.
    /// </summary>
    internal static void FilterRow(ReadOnlySpan<byte> alpha, int width, int y, int x0, int x1, int filter, Span<byte> values)
    {
        var row = alpha.Slice(y * width, width);
        if (filter == 0)
        {
            row[x0..x1].CopyTo(values);
            return;
        }

        var above = y == 0 ? default : alpha.Slice((y - 1) * width, width);
        var x = x0;
        if (y > 0 && Vector128.IsHardwareAccelerated)
        {
            if (x == 0)
            {
                values[0] = (byte)(row[0] - above[0]);
                x = 1;
            }

            for (; x <= x1 - Vector128<byte>.Count; x += Vector128<byte>.Count)
            {
                var current = Vector128.Create(row.Slice(x, Vector128<byte>.Count));
                Vector128<byte> prediction;
                if (filter == 1)
                {
                    prediction = Vector128.Create(row.Slice(x - 1, Vector128<byte>.Count));
                }
                else if (filter == 2)
                {
                    prediction = Vector128.Create(above.Slice(x, Vector128<byte>.Count));
                }
                else
                {
                    // Gradient: left + top - top-left clamped to 0-255, in 16-bit lanes
                    var left = Vector128.Create(row.Slice(x - 1, Vector128<byte>.Count));
                    var top = Vector128.Create(above.Slice(x, Vector128<byte>.Count));
                    var topLeft = Vector128.Create(above.Slice(x - 1, Vector128<byte>.Count));
                    prediction = Vector128.Narrow(Gradient(left, top, topLeft, upper: false), Gradient(left, top, topLeft, upper: true));
                }

                (current - prediction).CopyTo(values.Slice(x - x0, Vector128<byte>.Count));
            }
        }

        for (; x < x1; x++)
        {
            values[x - x0] = (byte)(row[x] - WebPAlphaDecoder.Predict(row, above, x, y, filter));
        }

        static Vector128<ushort> Gradient(Vector128<byte> left, Vector128<byte> top, Vector128<byte> topLeft, bool upper)
        {
            var sum = (Widen(left, upper) + Widen(top, upper)) - Widen(topLeft, upper);
            return Vector128.Min(Vector128.Max(sum, Vector128<short>.Zero), Vector128.Create((short)255)).AsUInt16();
        }

        static Vector128<short> Widen(Vector128<byte> value, bool upper) => (upper ? Vector128.WidenUpper(value) : Vector128.WidenLower(value)).AsInt16();
    }

    /// <summary>The scalar reference of <see cref="FilterRow"/>.</summary>
    internal static void FilterRowScalar(ReadOnlySpan<byte> alpha, int width, int y, int x0, int x1, int filter, Span<byte> values)
    {
        var row = alpha.Slice(y * width, width);
        var above = y == 0 ? default : alpha.Slice((y - 1) * width, width);
        for (var x = x0; x < x1; x++)
        {
            values[x - x0] = filter == 0 ? row[x] : (byte)(row[x] - WebPAlphaDecoder.Predict(row, above, x, y, filter));
        }
    }

    // The values in the green component of opaque pixels, 16 at a time
    private static void ToGreen(ReadOnlySpan<byte> values, Span<uint> argb)
    {
        var i = 0;
        if (Vector128.IsHardwareAccelerated)
        {
            var opaque = Vector128.Create(0xFF000000u);
            for (; i <= values.Length - Vector128<byte>.Count; i += Vector128<byte>.Count)
            {
                var bytes = Vector128.Create(values.Slice(i, Vector128<byte>.Count));
                var low = Vector128.WidenLower(bytes);
                var high = Vector128.WidenUpper(bytes);
                (Vector128.ShiftLeft(Vector128.WidenLower(low), 8) | opaque).CopyTo(argb.Slice(i, 4));
                (Vector128.ShiftLeft(Vector128.WidenUpper(low), 8) | opaque).CopyTo(argb.Slice(i + 4, 4));
                (Vector128.ShiftLeft(Vector128.WidenLower(high), 8) | opaque).CopyTo(argb.Slice(i + 8, 4));
                (Vector128.ShiftLeft(Vector128.WidenUpper(high), 8) | opaque).CopyTo(argb.Slice(i + 12, 4));
            }
        }

        for (; i < values.Length; i++)
        {
            argb[i] = 0xFF000000u | ((uint)values[i] << 8);
        }
    }
}
