using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

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
    /// With 128-bit hardware vector support, Up adds whole vectors; Sub reconstructs 16 bytes per step as a prefix sum
    /// inside the vector (modulo 256 additions are associative); Average and Paeth reconstruct one pixel per step for
    /// pixels of 3, 4, 6 and 8 bytes: the bytes of a pixel only depend on the previous pixel, so they are computed together
    /// in the lanes of one vector (16-bit lanes, whose intermediate values exceed a byte). For pixels of 1 and 2 bytes,
    /// Paeth runs a branch-free scalar loop.
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

        // The vector loops stop before the last 16 bytes; the scalar reference reconstructs the rest
        var done = 0;
        if (filterType == Sub)
        {
            done = bytesPerPixel switch
            {
                1 => UnfilterSubVector<PixelSize1>(row),
                2 => UnfilterSubVector<PixelSize2>(row),
                3 => UnfilterSubVector<PixelSize3>(row),
                4 => UnfilterSubVector<PixelSize4>(row),
                6 => UnfilterSubVector<PixelSize6>(row),
                8 => UnfilterSubVector<PixelSize8>(row),
                _ => 0,
            };
        }
        else if (filterType is Average or Paeth)
        {
            done = bytesPerPixel switch
            {
                1 or 2 when filterType == Paeth => UnfilterPaethBytes(row, previous, bytesPerPixel),
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

    /// <summary>
    /// Gets the adaptive cost (<see cref="GetAdaptiveCost"/>) of the scanline filtered with each of the five filter types,
    /// without writing the filtered bytes: one pass computes the five differences of every byte.
    /// </summary>
    /// <param name="row">The unfiltered bytes of the scanline.</param>
    /// <param name="previous">The unfiltered previous scanline of the same pass, at least as long as <paramref name="row"/>.</param>
    /// <param name="bytesPerPixel">The number of bytes per complete pixel, at least 1.</param>
    /// <param name="costs">The five costs, indexed by filter type.</param>
    /// <remarks>The result is identical to <see cref="GetAdaptiveCostsScalar"/>: <see cref="FilterScalar"/> then <see cref="GetAdaptiveCostScalar"/> for each type.</remarks>
    public static void GetAdaptiveCosts(ReadOnlySpan<byte> row, ReadOnlySpan<byte> previous, int bytesPerPixel, Span<long> costs)
    {
        costs = costs[..5];
        if (!Vector.IsHardwareAccelerated || row.Length < Vector<byte>.Count + bytesPerPixel)
        {
            GetAdaptiveCostsScalar(row, previous, bytesPerPixel, costs);
            return;
        }

        previous = previous[..row.Length];
        long none = 0, sub = 0, up = 0, average = 0, paeth = 0;

        // The first pixel: no left neighbor (a = c = 0)
        for (var i = 0; i < bytesPerPixel; i++)
        {
            none += Magnitude(row[i]);
            sub += Magnitude(row[i]);
            up += Magnitude((byte)(row[i] - previous[i]));
            average += Magnitude((byte)(row[i] - (previous[i] >> 1)));
            paeth += Magnitude((byte)(row[i] - previous[i]));
        }

        // |(sbyte)v| fits a byte; pairs of them are summed in 16-bit lanes, flushed before they can overflow
        var count = Vector<byte>.Count;
        var i0 = bytesPerPixel;
        while (i0 + count <= row.Length)
        {
            var noneSums = Vector<ushort>.Zero;
            var subSums = Vector<ushort>.Zero;
            var upSums = Vector<ushort>.Zero;
            var averageSums = Vector<ushort>.Zero;
            var paethSums = Vector<ushort>.Zero;
            for (var block = 0; block < 255 && i0 + count <= row.Length; block++, i0 += count)
            {
                var current = new Vector<byte>(row[i0..]);
                var left = new Vector<byte>(row[(i0 - bytesPerPixel)..]);
                var above = new Vector<byte>(previous[i0..]);
                var upperLeft = new Vector<byte>(previous[(i0 - bytesPerPixel)..]);
                noneSums += WidenedMagnitudes(current);
                subSums += WidenedMagnitudes(current - left);
                upSums += WidenedMagnitudes(current - above);
                averageSums += WidenedMagnitudes(current - ((left & above) + Vector.ShiftRightLogical(left ^ above, 1)));
                paethSums += WidenedMagnitudes(current - PaethVector(left, above, upperLeft));
            }

            none += Total(noneSums);
            sub += Total(subSums);
            up += Total(upSums);
            average += Total(averageSums);
            paeth += Total(paethSums);
        }

        for (var i = i0; i < row.Length; i++)
        {
            var left = row[i - bytesPerPixel];
            none += Magnitude(row[i]);
            sub += Magnitude((byte)(row[i] - left));
            up += Magnitude((byte)(row[i] - previous[i]));
            average += Magnitude((byte)(row[i] - ((left + previous[i]) >> 1)));
            paeth += Magnitude((byte)(row[i] - PaethPredictor(left, previous[i], previous[i - bytesPerPixel])));
        }

        costs[None] = none;
        costs[Sub] = sub;
        costs[Up] = up;
        costs[Average] = average;
        costs[Paeth] = paeth;

        static int Magnitude(byte value) => Math.Abs((int)(sbyte)value);

        static Vector<ushort> WidenedMagnitudes(Vector<byte> filtered)
        {
            var magnitudes = Vector.AsVectorByte(Vector.Abs(Vector.AsVectorSByte(filtered)));
            Vector.Widen(magnitudes, out var low, out var high);
            return low + high;
        }

        static long Total(Vector<ushort> sums)
        {
            Vector.Widen(sums, out var low, out var high);
            return (long)Vector.Sum(low + high);
        }
    }

    /// <summary>The scalar reference of <see cref="GetAdaptiveCosts"/>.</summary>
    internal static void GetAdaptiveCostsScalar(ReadOnlySpan<byte> row, ReadOnlySpan<byte> previous, int bytesPerPixel, Span<long> costs)
    {
        var filtered = new byte[row.Length];
        for (byte type = None; type <= Paeth; type++)
        {
            FilterScalar(type, row, previous, bytesPerPixel, filtered);
            costs[type] = GetAdaptiveCostScalar(filtered);
        }
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
    /// Reconstructs a Sub scanline 16 bytes at a time: each byte is the sum (modulo 256) of the filtered bytes at distances
    /// 0, bpp, 2 bpp, ... in the vector (shifted copies added for distances bpp, 2 bpp, 4 bpp and 8 bpp below 16), plus the
    /// reconstructed byte of the same channel in the last pixel of the previous vector.
    /// </summary>
    /// <returns>The number of bytes reconstructed (a multiple of 16).</returns>
    private static int UnfilterSubVector<TSize>(Span<byte> row)
        where TSize : struct, IPixelSize
    {
        ref var current = ref unsafe(MemoryMarshal.GetReference(row));
        var length = (nuint)row.Length;
        var last = Vector128<byte>.Zero;
        nuint i = 0;
        for (; i + (nuint)Vector128<byte>.Count <= length; i += (nuint)Vector128<byte>.Count)
        {
            var value = unsafe(Vector128.LoadUnsafe(ref current, i));
            value += ShiftBytesUp(value, (byte)TSize.Value);
            if (TSize.Value < 8)
            {
                value += ShiftBytesUp(value, (byte)(2 * TSize.Value));
            }

            if (TSize.Value < 4)
            {
                value += ShiftBytesUp(value, (byte)(4 * TSize.Value));
            }

            if (TSize.Value < 2)
            {
                value += ShiftBytesUp(value, 8);
            }

            // Lane j continues the channel of lane 16 - bpp + (j mod bpp) of the previous vector
            value += TSize.Value switch
            {
                1 => Vector128.Shuffle(last, Vector128.Create((byte)15)),
                2 => Vector128.Shuffle(last.AsUInt16(), Vector128.Create((ushort)7)).AsByte(),
                3 => Vector128.Shuffle(last, Vector128.Create((byte)13, 14, 15, 13, 14, 15, 13, 14, 15, 13, 14, 15, 13, 14, 15, 13)),
                4 => Vector128.Shuffle(last.AsUInt32(), Vector128.Create(3u)).AsByte(),
                6 => Vector128.Shuffle(last, Vector128.Create((byte)10, 11, 12, 13, 14, 15, 10, 11, 12, 13, 14, 15, 10, 11, 12, 13)),
                _ => Vector128.Shuffle(last.AsUInt64(), Vector128.Create(1ul)).AsByte(),
            };
            unsafe { value.StoreUnsafe(ref current, i); }
            last = value;
        }

        return (int)i;
    }

    /// <summary>Moves the bytes of a vector <paramref name="count"/> lanes up (to higher indices), shifting in zeros.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [SuppressMessage("Performance", "CA1857:A constant is expected for the parameter", Justification = "The callers pass multiples of TSize.Value, a JIT-time constant once the generic loop is specialized and this method inlined.")]
    private static Vector128<byte> ShiftBytesUp(Vector128<byte> value, byte count)
    {
        if (Sse2.IsSupported)
            return Sse2.ShiftLeftLogical128BitLane(value, count);

        if (AdvSimd.IsSupported)
            return AdvSimd.ExtractVector128(Vector128<byte>.Zero, value, (byte)(Vector128<byte>.Count - count));

        // Lanes below count get an index of 240 or more, which selects zero
        return Vector128.Shuffle(value, Vector128<byte>.Indices - Vector128.Create(count));
    }

    /// <summary>
    /// Reconstructs a Paeth scanline of 1- or 2-byte pixels: each byte depends on the byte just reconstructed, so the loop is
    /// scalar, without bounds checks, and the predictor is chosen without branches (random data defeats the predictor of
    /// the reference's branches).
    /// </summary>
    /// <returns>The number of bytes reconstructed (the whole row).</returns>
    private static int UnfilterPaethBytes(Span<byte> row, ReadOnlySpan<byte> previous, int bytesPerPixel)
    {
        Debug.Assert(bytesPerPixel is 1 or 2 && row.Length >= bytesPerPixel && previous.Length == row.Length);
        ref var current = ref unsafe(MemoryMarshal.GetReference(row));
        ref var above = ref unsafe(MemoryMarshal.GetReference(previous));
        var length = (nint)row.Length;
        var bpp = (nint)bytesPerPixel;

        // With a = c = 0 (first pixel) the predictor is the byte above
        for (nint i = 0; i < bpp; i++)
        {
            unsafe { Unsafe.Add(ref current, i) += Unsafe.Add(ref above, i); }
        }

        for (var i = bpp; i < length; i++)
        {
            int a, b, c;
            unsafe
            {
                a = Unsafe.Add(ref current, i - bpp);
                b = Unsafe.Add(ref above, i);
                c = Unsafe.Add(ref above, i - bpp);
            }

            // |p - a| = |b - c|, |p - b| = |a - c| and |p - c| = |(a - c) + (b - c)|, as in the scalar predictor
            var distanceA = Abs(b - c);
            var distanceB = Abs(a - c);
            var distanceC = Abs(b - c + a - c);
            var useA = (distanceA <= distanceB) & (distanceA <= distanceC);
            var predictor = distanceB <= distanceC ? b : c;
            predictor = useA ? a : predictor;
            unsafe { Unsafe.Add(ref current, i) += (byte)predictor; }
        }

        return row.Length;

        static int Abs(int value)
        {
            var sign = value >> 31;
            return (value ^ sign) - sign;
        }
    }

    /// <summary>
    /// Reconstructs the pixels of an Average or Paeth scanline whose 16 following bytes exist (whole vectors are loaded;
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
        if (filterType == Average)
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

    private readonly struct PixelSize1 : IPixelSize
    {
        public static int Value => 1;
    }

    private readonly struct PixelSize2 : IPixelSize
    {
        public static int Value => 2;
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
