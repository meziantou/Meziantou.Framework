using System.Numerics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Applies and reverses the five PNG filter types of filter method 0 (W3C PNG specification, section 7.3 and 9): None, Sub,
/// Up, Average and Paeth. Arithmetic is modulo 256 on bytes, independently of the bit depth; the bytes "before" the first
/// pixel and the row "above" the first scanline of a pass are zero.
/// </summary>
internal static class PngFilters
{
    public const byte None = 0;
    public const byte Sub = 1;
    public const byte Up = 2;
    public const byte Average = 3;
    public const byte Paeth = 4;

    /// <summary>Reconstructs a filtered scanline in place.</summary>
    /// <param name="filterType">The filter type byte of the scanline.</param>
    /// <param name="row">The filtered bytes (filter byte excluded); reconstructed in place.</param>
    /// <param name="previous">The reconstructed previous scanline of the same pass (all zero for the first one), same length as <paramref name="row"/>.</param>
    /// <param name="bytesPerPixel">The number of bytes per complete pixel, at least 1.</param>
    /// <exception cref="InvalidImageContentException">The filter type is not 0 to 4.</exception>
    public static void Unfilter(byte filterType, Span<byte> row, ReadOnlySpan<byte> previous, int bytesPerPixel)
    {
        previous = previous[..row.Length];
        switch (filterType)
        {
            case None:
                break;

            case Sub:
                for (var i = bytesPerPixel; i < row.Length; i++)
                {
                    row[i] += row[i - bytesPerPixel];
                }

                break;

            case Up:
                for (var i = 0; i < row.Length; i++)
                {
                    row[i] += previous[i];
                }

                break;

            case Average:
            {
                var first = Math.Min(bytesPerPixel, row.Length);
                for (var i = 0; i < first; i++)
                {
                    row[i] += (byte)(previous[i] >> 1);
                }

                for (var i = first; i < row.Length; i++)
                {
                    row[i] += (byte)((row[i - bytesPerPixel] + previous[i]) >> 1);
                }

                break;
            }

            case Paeth:
            {
                // With a = c = 0 (first pixel) the predictor is the byte above
                var first = Math.Min(bytesPerPixel, row.Length);
                for (var i = 0; i < first; i++)
                {
                    row[i] += previous[i];
                }

                for (var i = first; i < row.Length; i++)
                {
                    row[i] += PaethPredictor(row[i - bytesPerPixel], previous[i], previous[i - bytesPerPixel]);
                }

                break;
            }

            default:
                throw new InvalidImageContentException(string.Create(CultureInfo.InvariantCulture, $"The PNG filter type {filterType} is invalid (0 to 4)."), ImageFormat.Png);
        }
    }

    /// <summary>Filters a scanline (the encoder side of <see cref="Unfilter"/>).</summary>
    /// <param name="filterType">The filter type, 0 to 4.</param>
    /// <param name="row">The unfiltered bytes of the scanline (filter byte excluded).</param>
    /// <param name="previous">The unfiltered previous scanline of the same pass (all zero for the first one), at least as long as <paramref name="row"/>.</param>
    /// <param name="bytesPerPixel">The number of bytes per complete pixel, at least 1.</param>
    /// <param name="destination">The filtered bytes, same length as <paramref name="row"/>.</param>
    /// <remarks>
    /// With hardware vector support, every filter type is computed <see cref="Vector{T}.Count"/> bytes at a time (no
    /// dependency between output bytes on the encoder side); the result is identical to <see cref="FilterScalar"/>, the
    /// reference.
    /// </remarks>
    public static void Filter(byte filterType, ReadOnlySpan<byte> row, ReadOnlySpan<byte> previous, int bytesPerPixel, Span<byte> destination)
    {
        if (!Vector.IsHardwareAccelerated || row.Length < Vector<byte>.Count + bytesPerPixel || filterType > Paeth)
        {
            FilterScalar(filterType, row, previous, bytesPerPixel, destination);
            return;
        }

        previous = previous[..row.Length];
        destination = destination[..row.Length];
        var first = bytesPerPixel;
        switch (filterType)
        {
            case None:
                row.CopyTo(destination);
                return;

            case Up:
                FilterUpVector(row, previous, destination);
                return;

            case Sub:
                row[..first].CopyTo(destination);
                break;

            case Average:
                for (var i = 0; i < first; i++)
                {
                    destination[i] = (byte)(row[i] - (previous[i] >> 1));
                }

                break;

            default:
                for (var i = 0; i < first; i++)
                {
                    destination[i] = (byte)(row[i] - previous[i]);
                }

                break;
        }

        // Bytes from the second pixel on: left = row[i - bpp], above = previous[i], upper left = previous[i - bpp]
        var count = Vector<byte>.Count;
        var i0 = first;
        for (; i0 + count <= row.Length; i0 += count)
        {
            var current = new Vector<byte>(row[i0..]);
            var left = new Vector<byte>(row[(i0 - first)..]);
            Vector<byte> predictor;
            if (filterType == Sub)
            {
                predictor = left;
            }
            else if (filterType == Average)
            {
                // floor((left + above) / 2) without overflow
                var above = new Vector<byte>(previous[i0..]);
                predictor = (left & above) + Vector.ShiftRightLogical(left ^ above, 1);
            }
            else
            {
                predictor = PaethVector(left, new Vector<byte>(previous[i0..]), new Vector<byte>(previous[(i0 - first)..]));
            }

            (current - predictor).CopyTo(destination[i0..]);
        }

        for (var i = i0; i < row.Length; i++)
        {
            var left = row[i - first];
            destination[i] = filterType switch
            {
                Sub => (byte)(row[i] - left),
                Average => (byte)(row[i] - ((left + previous[i]) >> 1)),
                _ => (byte)(row[i] - PaethPredictor(left, previous[i], previous[i - first])),
            };
        }
    }

    /// <summary>The scalar reference of <see cref="Filter"/>.</summary>
    internal static void FilterScalar(byte filterType, ReadOnlySpan<byte> row, ReadOnlySpan<byte> previous, int bytesPerPixel, Span<byte> destination)
    {
        previous = previous[..row.Length];
        destination = destination[..row.Length];
        var first = Math.Min(bytesPerPixel, row.Length);
        switch (filterType)
        {
            case None:
                row.CopyTo(destination);
                break;

            case Sub:
                row[..first].CopyTo(destination);
                for (var i = first; i < row.Length; i++)
                {
                    destination[i] = (byte)(row[i] - row[i - bytesPerPixel]);
                }

                break;

            case Up:
                for (var i = 0; i < row.Length; i++)
                {
                    destination[i] = (byte)(row[i] - previous[i]);
                }

                break;

            case Average:
                for (var i = 0; i < first; i++)
                {
                    destination[i] = (byte)(row[i] - (previous[i] >> 1));
                }

                for (var i = first; i < row.Length; i++)
                {
                    destination[i] = (byte)(row[i] - ((row[i - bytesPerPixel] + previous[i]) >> 1));
                }

                break;

            case Paeth:
                for (var i = 0; i < first; i++)
                {
                    destination[i] = (byte)(row[i] - previous[i]);
                }

                for (var i = first; i < row.Length; i++)
                {
                    destination[i] = (byte)(row[i] - PaethPredictor(row[i - bytesPerPixel], previous[i], previous[i - bytesPerPixel]));
                }

                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(filterType), filterType, "The PNG filter type must be 0 to 4.");
        }
    }

    /// <summary>
    /// Gets the cost of a filtered scanline for the adaptive heuristic recommended by the PNG specification (section 12.8):
    /// the sum of the filtered bytes taken as signed differences, in absolute value.
    /// </summary>
    /// <param name="filtered">The filtered bytes.</param>
    /// <returns>The cost.</returns>
    public static long GetAdaptiveCost(ReadOnlySpan<byte> filtered)
    {
        if (!Vector.IsHardwareAccelerated || filtered.Length < Vector<byte>.Count)
            return GetAdaptiveCostScalar(filtered);

        // |(sbyte)v| fits a byte (|-128| = 128); pairs of them are summed in 16-bit lanes, flushed before they can overflow
        var count = Vector<byte>.Count;
        long total = 0;
        var i = 0;
        while (i + count <= filtered.Length)
        {
            var sums = Vector<ushort>.Zero;
            for (var block = 0; block < 255 && i + count <= filtered.Length; block++, i += count)
            {
                var magnitudes = Vector.AsVectorByte(Vector.Abs(Vector.AsVectorSByte(new Vector<byte>(filtered[i..]))));
                Vector.Widen(magnitudes, out var low, out var high);
                sums += low + high;
            }

            Vector.Widen(sums, out var lowSums, out var highSums);
            total += (long)Vector.Sum(lowSums + highSums);
        }

        return total + GetAdaptiveCostScalar(filtered[i..]);
    }

    /// <summary>The scalar reference of <see cref="GetAdaptiveCost"/>.</summary>
    internal static long GetAdaptiveCostScalar(ReadOnlySpan<byte> filtered)
    {
        long sum = 0;
        foreach (var value in filtered)
        {
            sum += Math.Abs((int)(sbyte)value);
        }

        return sum;
    }

    private static void FilterUpVector(ReadOnlySpan<byte> row, ReadOnlySpan<byte> previous, Span<byte> destination)
    {
        var count = Vector<byte>.Count;
        var i = 0;
        for (; i + count <= row.Length; i += count)
        {
            (new Vector<byte>(row[i..]) - new Vector<byte>(previous[i..])).CopyTo(destination[i..]);
        }

        for (; i < row.Length; i++)
        {
            destination[i] = (byte)(row[i] - previous[i]);
        }
    }

    /// <summary>
    /// The Paeth predictor of <see cref="Vector{T}.Count"/> bytes, in 16-bit lanes: with <c>p = a + b - c</c>, the distances
    /// are <c>|p - a| = |b - c|</c>, <c>|p - b| = |a - c|</c> and <c>|p - c| = |a + b - 2c|</c>; ties prefer a, then b.
    /// </summary>
    private static Vector<byte> PaethVector(Vector<byte> left, Vector<byte> above, Vector<byte> upperLeft)
    {
        Vector.Widen(left, out var leftLow, out var leftHigh);
        Vector.Widen(above, out var aboveLow, out var aboveHigh);
        Vector.Widen(upperLeft, out var upperLeftLow, out var upperLeftHigh);
        var low = Paeth(Vector.AsVectorInt16(leftLow), Vector.AsVectorInt16(aboveLow), Vector.AsVectorInt16(upperLeftLow));
        var high = Paeth(Vector.AsVectorInt16(leftHigh), Vector.AsVectorInt16(aboveHigh), Vector.AsVectorInt16(upperLeftHigh));
        return Vector.Narrow(Vector.AsVectorUInt16(low), Vector.AsVectorUInt16(high));

        static Vector<short> Paeth(Vector<short> a, Vector<short> b, Vector<short> c)
        {
            var distanceA = Vector.Abs(b - c);
            var distanceB = Vector.Abs(a - c);
            var distanceC = Vector.Abs(a + b - c - c);
            var useA = Vector.LessThanOrEqual(distanceA, distanceB) & Vector.LessThanOrEqual(distanceA, distanceC);
            var useB = Vector.LessThanOrEqual(distanceB, distanceC);
            return Vector.ConditionalSelect(useA, a, Vector.ConditionalSelect(useB, b, c));
        }
    }

    /// <summary>The Paeth predictor of the PNG specification: the neighbor (left, above, upper left) closest to <c>a + b - c</c>, ties in that order.</summary>
    public static byte PaethPredictor(byte left, byte above, byte upperLeft)
    {
        var estimate = left + above - upperLeft;
        var distanceLeft = Math.Abs(estimate - left);
        var distanceAbove = Math.Abs(estimate - above);
        var distanceUpperLeft = Math.Abs(estimate - upperLeft);
        if (distanceLeft <= distanceAbove && distanceLeft <= distanceUpperLeft)
            return left;

        return distanceAbove <= distanceUpperLeft ? above : upperLeft;
    }
}
