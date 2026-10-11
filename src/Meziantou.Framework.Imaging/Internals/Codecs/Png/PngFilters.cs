using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

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
    /// <remarks>
    /// With 128-bit hardware vector support, Up adds whole vectors, and Sub, Average and Paeth reconstruct one pixel per
    /// step for pixels of 3, 4, 6 and 8 bytes: the bytes of a pixel only depend on the previous pixel, so they are computed
    /// together in the lanes of one vector (16-bit lanes for Average and Paeth, whose intermediate values exceed a byte).
    /// The result is identical to <see cref="UnfilterScalar(byte, Span{byte}, ReadOnlySpan{byte}, int)"/>, the reference.
    /// </remarks>
    public static void Unfilter(byte filterType, Span<byte> row, ReadOnlySpan<byte> previous, int bytesPerPixel)
    {
        if (!UseVectors || row.Length < Vector128<byte>.Count)
        {
            UnfilterScalar(filterType, row, previous, bytesPerPixel);
            return;
        }

        previous = previous[..row.Length];
        if (filterType == Up)
        {
            UnfilterUpVector(row, previous);
            return;
        }

        // The pixel-at-a-time loops stop before the last 16 bytes; the scalar reference reconstructs the rest
        var done = 0;
        if (filterType is Sub or Average or Paeth)
        {
            done = bytesPerPixel switch
            {
                3 => UnfilterPixels<PixelSize3>(filterType, row, previous),
                4 => UnfilterPixels<PixelSize4>(filterType, row, previous),
                6 => UnfilterPixels<PixelSize6>(filterType, row, previous),
                8 => UnfilterPixels<PixelSize8>(filterType, row, previous),
                _ => 0,
            };
        }

        UnfilterScalar(filterType, row, previous, bytesPerPixel, done);
    }

    /// <summary>The scalar reference of <see cref="Unfilter"/>.</summary>
    internal static void UnfilterScalar(byte filterType, Span<byte> row, ReadOnlySpan<byte> previous, int bytesPerPixel)
        => UnfilterScalar(filterType, row, previous, bytesPerPixel, start: 0);

    /// <summary>Reconstructs the bytes of a scanline from <paramref name="start"/> (a pixel boundary); the bytes before it are already reconstructed.</summary>
    private static void UnfilterScalar(byte filterType, Span<byte> row, ReadOnlySpan<byte> previous, int bytesPerPixel, int start)
    {
        previous = previous[..row.Length];
        switch (filterType)
        {
            case None:
                break;

            case Sub:
                for (var i = Math.Max(start, bytesPerPixel); i < row.Length; i++)
                {
                    row[i] += row[i - bytesPerPixel];
                }

                break;

            case Up:
                for (var i = start; i < row.Length; i++)
                {
                    row[i] += previous[i];
                }

                break;

            case Average:
            {
                var first = Math.Min(bytesPerPixel, row.Length);
                for (var i = start; i < first; i++)
                {
                    row[i] += (byte)(previous[i] >> 1);
                }

                for (var i = Math.Max(start, first); i < row.Length; i++)
                {
                    row[i] += (byte)((row[i - bytesPerPixel] + previous[i]) >> 1);
                }

                break;
            }

            case Paeth:
            {
                // With a = c = 0 (first pixel) the predictor is the byte above
                var first = Math.Min(bytesPerPixel, row.Length);
                for (var i = start; i < first; i++)
                {
                    row[i] += previous[i];
                }

                for (var i = Math.Max(start, first); i < row.Length; i++)
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

    private static bool UseVectors => Vector128.IsHardwareAccelerated && BitConverter.IsLittleEndian;

    private static void UnfilterUpVector(Span<byte> row, ReadOnlySpan<byte> previous)
    {
        ref var current = ref unsafe(MemoryMarshal.GetReference(row));
        ref var above = ref unsafe(MemoryMarshal.GetReference(previous));
        var length = (nuint)row.Length;
        nuint i = 0;
        if (Vector.IsHardwareAccelerated)
        {
            var count = (nuint)Vector<byte>.Count;
            for (; i + count <= length; i += count)
            {
                unsafe { (Vector.LoadUnsafe(ref current, i) + Vector.LoadUnsafe(ref above, i)).StoreUnsafe(ref current, i); }
            }
        }

        for (; i < length; i++)
        {
            unsafe { Unsafe.Add(ref current, i) += Unsafe.Add(ref above, i); }
        }
    }

    /// <summary>
    /// Reconstructs the pixels of a Sub, Average or Paeth scanline whose 16 following bytes exist (whole vectors are loaded;
    /// only the bytes of the pixel are stored), one pixel per step.
    /// </summary>
    /// <returns>The number of bytes reconstructed (a multiple of the pixel size).</returns>
    private static int UnfilterPixels<TSize>(byte filterType, Span<byte> row, ReadOnlySpan<byte> previous)
        where TSize : struct, IPixelSize
    {
        ref var current = ref unsafe(MemoryMarshal.GetReference(row));
        ref var above = ref unsafe(MemoryMarshal.GetReference(previous));
        var step = (nuint)TSize.Value;
        var last = (nuint)(row.Length - Vector128<byte>.Count);
        nuint i = 0;
        if (filterType == Sub)
        {
            var left = Vector128<byte>.Zero;
            for (; i <= last; i += step)
            {
                left += unsafe(Vector128.LoadUnsafe(ref current, i));
                StorePixel<TSize>(ref unsafe(Unsafe.Add(ref current, i)), left);
            }
        }
        else if (filterType == Average)
        {
            // floor((left + above) / 2) needs 9 bits: 16-bit lanes, the sum reduced modulo 256 before it is the next "left"
            var byteMask = Vector128.Create((ushort)0xFF);
            var left = Vector128<ushort>.Zero;
            for (; i <= last; i += step)
            {
                var b = Vector128.WidenLower(unsafe(Vector128.LoadUnsafe(ref above, i)));
                var x = Vector128.WidenLower(unsafe(Vector128.LoadUnsafe(ref current, i)));
                left = (x + ((left + b) >>> 1)) & byteMask;
                StorePixel<TSize>(ref unsafe(Unsafe.Add(ref current, i)), Vector128.Narrow(left, left));
            }
        }
        else
        {
            // With p = a + b - c: |p - a| = |b - c|, |p - b| = |a - c| and |p - c| = |(a - c) + (b - c)|; ties prefer a, then b.
            // The first pixel has a = c = 0, which selects b (or a = b = 0): the byte above, as the specification requires
            var byteMask = Vector128.Create((short)0xFF);
            var a = Vector128<short>.Zero;
            var c = Vector128<short>.Zero;
            for (; i <= last; i += step)
            {
                var b = Vector128.WidenLower(unsafe(Vector128.LoadUnsafe(ref above, i))).AsInt16();
                var x = Vector128.WidenLower(unsafe(Vector128.LoadUnsafe(ref current, i))).AsInt16();
                var deltaA = b - c;
                var deltaB = a - c;
                var distanceA = Vector128.Abs(deltaA);
                var distanceB = Vector128.Abs(deltaB);
                var distanceC = Vector128.Abs(deltaA + deltaB);
                var useA = Vector128.LessThanOrEqual(distanceA, distanceB) & Vector128.LessThanOrEqual(distanceA, distanceC);
                var useB = Vector128.LessThanOrEqual(distanceB, distanceC);
                var predictor = Vector128.ConditionalSelect(useA, a, Vector128.ConditionalSelect(useB, b, c));
                a = (x + predictor) & byteMask;
                c = b;
                StorePixel<TSize>(ref unsafe(Unsafe.Add(ref current, i)), Vector128.Narrow(a.AsUInt16(), a.AsUInt16()));
            }
        }

        return (int)i;
    }

    /// <summary>Stores the first <c>TSize.Value</c> bytes of a vector (the size is a JIT-time constant of each instantiation).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void StorePixel<TSize>(ref byte destination, Vector128<byte> pixel)
        where TSize : struct, IPixelSize
    {
        if (TSize.Value == 3)
        {
            unsafe
            {
                Unsafe.WriteUnaligned(ref destination, pixel.AsUInt16().ToScalar());
                Unsafe.Add(ref destination, 2) = pixel.GetElement(2);
            }
        }
        else if (TSize.Value == 4)
        {
            unsafe { Unsafe.WriteUnaligned(ref destination, pixel.AsUInt32().ToScalar()); }
        }
        else if (TSize.Value == 6)
        {
            unsafe
            {
                Unsafe.WriteUnaligned(ref destination, pixel.AsUInt32().ToScalar());
                Unsafe.WriteUnaligned(ref Unsafe.Add(ref destination, 4), pixel.AsUInt16().GetElement(2));
            }
        }
        else
        {
            unsafe { Unsafe.WriteUnaligned(ref destination, pixel.AsUInt64().ToScalar()); }
        }
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

    /// <summary>A pixel size as a type argument, so that each size gets its own specialized loop.</summary>
    private interface IPixelSize
    {
        static abstract int Value { get; }
    }

    private readonly struct PixelSize3 : IPixelSize
    {
        public static int Value => 3;
    }

    private readonly struct PixelSize4 : IPixelSize
    {
        public static int Value => 4;
    }

    private readonly struct PixelSize6 : IPixelSize
    {
        public static int Value => 6;
    }

    private readonly struct PixelSize8 : IPixelSize
    {
        public static int Value => 8;
    }
}
