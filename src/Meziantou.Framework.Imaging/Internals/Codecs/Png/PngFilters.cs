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
    /// in the lanes of one vector (Average in 16-bit lanes, whose intermediate values exceed a byte; on Arm64, it stays in
    /// byte lanes with a halving add). Paeth first computes, for a piece of the row, the thresholds of the left byte that
    /// select each predictor (they only depend on the row above), so that each pixel then costs three comparisons and two
    /// selections (<see cref="UnfilterPaethPixels{TSize}"/>). For pixels of 1 and 2 bytes, Paeth runs a branch-free scalar loop.
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
        else if (filterType == Average)
        {
            done = bytesPerPixel switch
            {
                3 => UnfilterAverage<PixelSize3>(row, previous),
                4 => UnfilterAverage<PixelSize4>(row, previous),
                6 => UnfilterAverage<PixelSize6>(row, previous),
                8 => UnfilterAverage<PixelSize8>(row, previous),
                _ => 0,
            };
        }
        else if (filterType == Paeth)
        {
            done = bytesPerPixel switch
            {
                1 or 2 => UnfilterPaethBytes(row, previous, bytesPerPixel),
                3 => UnfilterPaethPixels<PixelSize3>(row, previous),
                4 => UnfilterPaethPixels<PixelSize4>(row, previous),
                6 => UnfilterPaethPixels<PixelSize6>(row, previous),
                8 => UnfilterPaethPixels<PixelSize8>(row, previous),
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

        // |(sbyte)v| fits a byte (|-128| = 128); pairs of them are summed in 16-bit lanes, flushed before they can overflow.
        // One chain of sums: the widening adds of AddMagnitudes would wait for each other (it suits the five chains of GetAdaptiveCosts)
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
        => GetAdaptiveCosts(row, previous, bytesPerPixel, costs, paeth: default);

    /// <summary>
    /// Gets the adaptive costs of the five filter types (<see cref="GetAdaptiveCosts(ReadOnlySpan{byte}, ReadOnlySpan{byte}, int, Span{long})"/>)
    /// and writes the scanline filtered with Paeth, the filter type that costs the most to compute again.
    /// </summary>
    /// <param name="row">The unfiltered bytes of the scanline.</param>
    /// <param name="previous">The unfiltered previous scanline of the same pass, at least as long as <paramref name="row"/>.</param>
    /// <param name="bytesPerPixel">The number of bytes per complete pixel, at least 1.</param>
    /// <param name="costs">The five costs, indexed by filter type.</param>
    /// <param name="paeth">The bytes filtered with Paeth, as <see cref="Filter"/> writes them, or empty to skip them.</param>
    public static void GetAdaptiveCosts(ReadOnlySpan<byte> row, ReadOnlySpan<byte> previous, int bytesPerPixel, Span<long> costs, Span<byte> paeth)
    {
        costs = costs[..5];
        var writePaeth = !paeth.IsEmpty;
        if (writePaeth)
        {
            paeth = paeth[..row.Length];
        }

        if (!Vector.IsHardwareAccelerated || row.Length < Vector<byte>.Count + bytesPerPixel)
        {
            GetAdaptiveCostsScalar(row, previous, bytesPerPixel, costs);
            if (writePaeth)
            {
                FilterScalar(Paeth, row, previous, bytesPerPixel, paeth);
            }

            return;
        }

        previous = previous[..row.Length];
        long none = 0, sub = 0, up = 0, average = 0, paethCost = 0;

        // The first pixel: no left neighbor (a = c = 0)
        for (var i = 0; i < bytesPerPixel; i++)
        {
            none += Magnitude(row[i]);
            sub += Magnitude(row[i]);
            up += Magnitude((byte)(row[i] - previous[i]));
            average += Magnitude((byte)(row[i] - (previous[i] >> 1)));
            var paethByte = (byte)(row[i] - previous[i]);
            paethCost += Magnitude(paethByte);
            if (writePaeth)
            {
                paeth[i] = paethByte;
            }
        }

        // |(sbyte)v| fits a byte; the partial sums are flushed before they can overflow (AddMagnitudes)
        var count = Vector<byte>.Count;
        var i0 = bytesPerPixel;
        ref var rowStart = ref unsafe(MemoryMarshal.GetReference(row));
        ref var previousStart = ref unsafe(MemoryMarshal.GetReference(previous));
        ref var paethStart = ref unsafe(MemoryMarshal.GetReference(paeth));
        var distance = (nuint)bytesPerPixel;
        while (i0 + count <= row.Length)
        {
            var noneSums = Vector<ushort>.Zero;
            var subSums = Vector<ushort>.Zero;
            var upSums = Vector<ushort>.Zero;
            var averageSums = Vector<ushort>.Zero;
            var paethSums = Vector<ushort>.Zero;
            for (var block = 0; block < 255 && i0 + count <= row.Length; block++, i0 += count)
            {
                var offset = (nuint)i0;
                Vector<byte> current, left, above, upperLeft;
                unsafe
                {
                    current = Vector.LoadUnsafe(ref rowStart, offset);
                    left = Vector.LoadUnsafe(ref rowStart, offset - distance);
                    above = Vector.LoadUnsafe(ref previousStart, offset);
                    upperLeft = Vector.LoadUnsafe(ref previousStart, offset - distance);
                }

                noneSums = AddMagnitudes(noneSums, current);
                subSums = AddMagnitudes(subSums, current - left);
                upSums = AddMagnitudes(upSums, current - above);
                averageSums = AddMagnitudes(averageSums, current - ((left & above) + Vector.ShiftRightLogical(left ^ above, 1)));
                var paethFiltered = current - PaethVector(left, above, upperLeft);
                paethSums = AddMagnitudes(paethSums, paethFiltered);
                if (writePaeth)
                {
                    unsafe { paethFiltered.StoreUnsafe(ref paethStart, offset); }
                }
            }

            none += TotalMagnitudes(noneSums);
            sub += TotalMagnitudes(subSums);
            up += TotalMagnitudes(upSums);
            average += TotalMagnitudes(averageSums);
            paethCost += TotalMagnitudes(paethSums);
        }

        for (var i = i0; i < row.Length; i++)
        {
            var left = row[i - bytesPerPixel];
            none += Magnitude(row[i]);
            sub += Magnitude((byte)(row[i] - left));
            up += Magnitude((byte)(row[i] - previous[i]));
            average += Magnitude((byte)(row[i] - ((left + previous[i]) >> 1)));
            var paethByte = (byte)(row[i] - PaethPredictor(left, previous[i], previous[i - bytesPerPixel]));
            paethCost += Magnitude(paethByte);
            if (writePaeth)
            {
                paeth[i] = paethByte;
            }
        }

        costs[None] = none;
        costs[Sub] = sub;
        costs[Up] = up;
        costs[Average] = average;
        costs[Paeth] = paethCost;

        static int Magnitude(byte value) => Math.Abs((int)(sbyte)value);
    }

    /// <summary>
    /// Gets a value indicating whether the partial sums of <see cref="AddMagnitudes"/> are 64-bit lanes of byte sums
    /// (<c>psadbw</c> against zero); otherwise they are 16-bit lanes of pairs of bytes.
    /// </summary>
    private static bool SumsMagnitudesIn64BitLanes => (Avx2.IsSupported && Vector<byte>.Count == Vector256<byte>.Count) || (Sse2.IsSupported && Vector<byte>.Count == Vector128<byte>.Count);

    /// <summary>
    /// Adds the magnitudes <c>|(sbyte)v|</c> of filtered bytes, at most 128 each, to partial sums that can take 255 calls
    /// before <see cref="TotalMagnitudes"/>: 16-bit lanes that each receive two magnitudes (a pairwise widening add on
    /// Arm64), or, on x64, 64-bit lanes that each receive eight (the sum of absolute differences with zero).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector<ushort> AddMagnitudes(Vector<ushort> sums, Vector<byte> filtered)
    {
        var magnitudes = Vector.AsVectorByte(Vector.Abs(Vector.AsVectorSByte(filtered)));
        if (AdvSimd.IsSupported && Vector<byte>.Count == Vector128<byte>.Count)
            return AdvSimd.AddPairwiseWideningAndAdd(sums.AsVector128(), magnitudes.AsVector128()).AsVector();

        if (Avx2.IsSupported && Vector<byte>.Count == Vector256<byte>.Count)
            return Vector.AsVectorUInt16(Vector.AsVectorUInt64(sums) + Avx2.SumAbsoluteDifferences(magnitudes.AsVector256(), Vector256<byte>.Zero).AsUInt64().AsVector());

        if (Sse2.IsSupported && Vector<byte>.Count == Vector128<byte>.Count)
            return Vector.AsVectorUInt16(Vector.AsVectorUInt64(sums) + Sse2.SumAbsoluteDifferences(magnitudes.AsVector128(), Vector128<byte>.Zero).AsUInt64().AsVector());

        Vector.Widen(magnitudes, out var low, out var high);
        return sums + low + high;
    }

    /// <summary>Adds up the partial sums of <see cref="AddMagnitudes"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long TotalMagnitudes(Vector<ushort> sums)
    {
        if (SumsMagnitudesIn64BitLanes)
            return (long)Vector.Sum(Vector.AsVectorUInt64(sums));

        Vector.Widen(sums, out var low, out var high);
        return (long)Vector.Sum(low + high);
    }

    /// <summary>The scalar reference of <see cref="GetAdaptiveCosts(ReadOnlySpan{byte}, ReadOnlySpan{byte}, int, Span{long})"/>.</summary>
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

    /// <summary>The length of the pieces of a Paeth scanline whose thresholds are computed at once: a multiple of 3, 4, 6, 8 and 16.</summary>
    private const nuint PaethPieceLength = 240;

    /// <summary>The length of each array of Paeth thresholds: the longest piece (less than one more vector) and the vector read at its last pixel.</summary>
    private const int PaethScratchStride = (int)PaethPieceLength + 32;

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
    /// Reconstructs the pixels of an Average scanline whose 16 following bytes exist (whole vectors are loaded; only the
    /// bytes of the pixel are stored), one pixel per step.
    /// </summary>
    /// <returns>The number of bytes reconstructed (a multiple of the pixel size).</returns>
    private static int UnfilterAverage<TSize>(Span<byte> row, ReadOnlySpan<byte> previous)
        where TSize : struct, IPixelSize
    {
        // Unfilter only calls this method for rows of at least one vector: "last" would wrap for a shorter row
        Debug.Assert(row.Length >= Vector128<byte>.Count);
        Debug.Assert(previous.Length >= row.Length);
        ref var current = ref unsafe(MemoryMarshal.GetReference(row));
        ref var above = ref unsafe(MemoryMarshal.GetReference(previous));
        var step = (nuint)TSize.Value;
        var last = (nuint)(row.Length - Vector128<byte>.Count);
        nuint i = 0;
        if (AdvSimd.IsSupported)
        {
            // The halving add computes floor((left + above) / 2) in byte lanes: two operations per pixel on the chain of "left"
            var left = Vector128<byte>.Zero;
            for (; i <= last; i += step)
            {
                var b = unsafe(Vector128.LoadUnsafe(ref above, i));
                var x = unsafe(Vector128.LoadUnsafe(ref current, i));
                left = x + AdvSimd.FusedAddHalving(left, b);
                StorePixel<TSize>(ref unsafe(Unsafe.Add(ref current, i)), left);
            }
        }
        else
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

        return (int)i;
    }

    /// <summary>
    /// Reconstructs a Paeth scanline of 3-, 4-, 6- or 8-byte pixels, piece by piece: a vector pass computes, from the row
    /// above alone, the thresholds of the left byte that select each predictor; a serial pass then reconstructs one pixel
    /// per step with three comparisons and two selections, instead of the whole predictor on the chain of "left".
    /// </summary>
    /// <remarks>
    /// <para>
    /// With <c>a</c> the left byte, <c>b</c> the byte above and <c>c</c> the upper left one, the predictor is <c>a</c> unless
    /// <c>a</c> is strictly between <c>b</c> and <c>e = 3c - 2b</c>; in that interval, it is the one of <c>b</c> and
    /// <c>c</c> on the side of <c>b</c> (ties included), which is the larger one when <c>a &gt;= m</c>, with <c>m</c> the
    /// middle of the interval rounded toward <c>c</c> (<c>b == c</c> gives an empty interval). The thresholds are clamped to
    /// bytes: the bounds of the interval as <c>a &lt; L</c> and <c>a &gt; H</c>, the middle as <c>a &gt; T</c>, and when
    /// <c>m &lt;= 0</c> the smaller value is replaced by the larger one, since <c>a &gt;= m</c> then always holds.
    /// </para>
    /// <para>
    /// The serial pass works in an offset domain (bytes plus 128, as signed bytes), where signed comparisons give the
    /// unsigned ones on every platform; it holds <c>x + a</c> and the sums of <c>x</c> with the two other predictors, so
    /// that the chain of "left" is one comparison, one OR and one selection long. The result is identical to
    /// <see cref="UnfilterScalar(byte, Span{byte}, ReadOnlySpan{byte}, int)"/> for every triple (tested exhaustively).
    /// </para>
    /// </remarks>
    /// <returns>The number of bytes reconstructed: the whole pixels of the row, none for a row shorter than two vectors.</returns>
    private static int UnfilterPaethPixels<TSize>(Span<byte> row, ReadOnlySpan<byte> previous)
        where TSize : struct, IPixelSize
    {
        Debug.Assert(previous.Length >= row.Length);

        // Two vectors at least: the last block of a piece, aligned on its end, never starts before the first pixel
        if (row.Length < 2 * Vector128<byte>.Count)
            return 0;

        Span<byte> scratch = stackalloc byte[PaethScratchStride * 6];
        ref var current = ref unsafe(MemoryMarshal.GetReference(row));
        ref var above = ref unsafe(MemoryMarshal.GetReference(previous));
        ref var lowBound = ref unsafe(MemoryMarshal.GetReference(scratch));
        ref var highBound = ref unsafe(Unsafe.Add(ref lowBound, PaethScratchStride));
        ref var middle = ref unsafe(Unsafe.Add(ref highBound, PaethScratchStride));
        ref var filtered = ref unsafe(Unsafe.Add(ref middle, PaethScratchStride));
        ref var plusLarger = ref unsafe(Unsafe.Add(ref filtered, PaethScratchStride));
        ref var plusSmaller = ref unsafe(Unsafe.Add(ref plusLarger, PaethScratchStride));

        var step = (nuint)TSize.Value;
        var length = (nuint)row.Length;
        var one = Vector128<byte>.One;
        var offset = Vector128.Create((byte)0x80);

        // The left pixel before the first one is zero (offset domain)
        var left = offset.AsSByte();
        nuint start = 0;
        nuint i = 0;
        while (start < length)
        {
            // A piece is a multiple of every pixel size; the last one takes the rest, so that it is at least one vector long
            var count = length - start >= PaethPieceLength + (nuint)Vector128<byte>.Count ? PaethPieceLength : length - start;
            nuint j = 0;
            while (true)
            {
                var position = start + j;
                var b = unsafe(Vector128.LoadUnsafe(ref above, position));
                var x = unsafe(Vector128.LoadUnsafe(ref current, position));

                // The bytes before the first pixel are zero
                var c = position == 0 ? ShiftBytesUp(b, (byte)TSize.Value) : unsafe(Vector128.LoadUnsafe(ref above, position - step));
                var below = Vector128.LessThan(b, c);
                var excess = Vector128.SubtractSaturate(b, c);
                var deficit = Vector128.SubtractSaturate(c, b);
                var halfExcess = excess >>> 1;

                // b >= c: the interval is (e, b) with e = c - 2 (b - c), and m - 1 = c - 1 - floor((b - c) / 2).
                // b < c: the interval is (b, e) with e = c + 2 (c - b), and m - 1 = c + floor((c - b) / 2)
                var low = Vector128.ConditionalSelect(below, b + one, Vector128.SubtractSaturate(Vector128.AddSaturate(c, one), Vector128.AddSaturate(excess, excess)));
                var high = Vector128.ConditionalSelect(below, Vector128.AddSaturate(c - one, Vector128.AddSaturate(deficit, deficit)), Vector128.SubtractSaturate(b, one));
                var threshold = Vector128.ConditionalSelect(below, Vector128.AddSaturate(c, deficit >>> 1), Vector128.SubtractSaturate(Vector128.SubtractSaturate(c, halfExcess), one));
                var larger = Vector128.Max(b, c);
                var smaller = Vector128.ConditionalSelect(Vector128.GreaterThanOrEqual(halfExcess, c), larger, Vector128.Min(b, c));
                unsafe
                {
                    (low ^ offset).StoreUnsafe(ref lowBound, j);
                    (high ^ offset).StoreUnsafe(ref highBound, j);
                    (threshold ^ offset).StoreUnsafe(ref middle, j);
                    x.StoreUnsafe(ref filtered, j);
                    ((x ^ offset) + larger).StoreUnsafe(ref plusLarger, j);
                    ((x ^ offset) + smaller).StoreUnsafe(ref plusSmaller, j);
                }

                if (j + (nuint)Vector128<byte>.Count == count)
                    break;

                // The last block ends with the piece; it may overlap the previous one (same bytes, same values)
                j = Math.Min(j + (nuint)Vector128<byte>.Count, count - (nuint)Vector128<byte>.Count);
            }

            // The bytes of the piece were copied: whole vectors are stored while they stay in its whole pixels (the
            // following pixels overwrite their extra lanes; the bytes of a last partial pixel are reconstructed after
            // this method, from the row), the last pixels alone
            var end = start + count;
            var vectorEnd = Math.Min(end, length - (length % step));
            for (; i + (nuint)Vector128<byte>.Count <= vectorEnd; i += step)
            {
                left = NextPaethPixel(left, ref unsafe(Unsafe.Add(ref lowBound, i - start)), PaethScratchStride);
                unsafe { (left ^ offset.AsSByte()).AsByte().StoreUnsafe(ref current, i); }
            }

            for (; i + step <= end; i += step)
            {
                left = NextPaethPixel(left, ref unsafe(Unsafe.Add(ref lowBound, i - start)), PaethScratchStride);
                StorePixel<TSize>(ref unsafe(Unsafe.Add(ref current, i)), (left ^ offset.AsSByte()).AsByte());
            }

            start += count;
        }

        return (int)i;
    }

    /// <summary>
    /// Reconstructs one Paeth pixel from its thresholds (<see cref="UnfilterPaethPixels{TSize}"/>), in the offset domain:
    /// <paramref name="thresholds"/> is its byte in the first of the six arrays, one stride apart.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<sbyte> NextPaethPixel(Vector128<sbyte> left, ref byte thresholds, nuint stride)
    {
        Vector128<sbyte> low, high, middle, filtered, plusLarger, plusSmaller;
        unsafe
        {
            low = Vector128.LoadUnsafe(ref thresholds).AsSByte();
            high = Vector128.LoadUnsafe(ref thresholds, stride).AsSByte();
            middle = Vector128.LoadUnsafe(ref thresholds, 2 * stride).AsSByte();
            filtered = Vector128.LoadUnsafe(ref thresholds, 3 * stride).AsSByte();
            plusLarger = Vector128.LoadUnsafe(ref thresholds, 4 * stride).AsSByte();
            plusSmaller = Vector128.LoadUnsafe(ref thresholds, 5 * stride).AsSByte();
        }

        var pickLeft = Vector128.GreaterThan(low, left) | Vector128.GreaterThan(left, high);
        var other = Vector128.ConditionalSelect(Vector128.GreaterThan(left, middle), plusLarger, plusSmaller);
        return Vector128.ConditionalSelect(pickLeft, filtered + left, other);
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
    /// The Paeth predictor of <see cref="Vector{T}.Count"/> bytes, in byte lanes: with <c>p = a + b - c</c>, the distances
    /// are <c>|p - a| = |b - c|</c>, <c>|p - b| = |a - c|</c> and <c>|p - c| = |(b - c) + (a - c)|</c>; ties prefer a, then b.
    /// </summary>
    /// <remarks>
    /// The first two distances fit a byte. The third is their sum when <c>b - c</c> and <c>a - c</c> have the same sign, and
    /// the difference of the larger and the smaller otherwise; the sum saturates at 255, which changes no comparison, since
    /// it is only compared with the first two distances, at most 255. The result is identical to
    /// <see cref="PaethVectorWide"/> and to <see cref="PaethPredictor"/>.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Vector<byte> PaethVector(Vector<byte> left, Vector<byte> above, Vector<byte> upperLeft)
    {
        if (AdvSimd.IsSupported && Vector<byte>.Count == Vector128<byte>.Count)
        {
            // Absolute differences, unsigned comparisons and the saturating add are single instructions
            var (a, b, c) = (left.AsVector128(), above.AsVector128(), upperLeft.AsVector128());
            var pa = AdvSimd.AbsoluteDifference(b, c);
            var pb = AdvSimd.AbsoluteDifference(a, c);
            var differentSigns = AdvSimd.CompareGreaterThanOrEqual(b, c) ^ AdvSimd.CompareGreaterThanOrEqual(a, c);
            var pc = AdvSimd.BitwiseSelect(differentSigns, AdvSimd.AbsoluteDifference(pa, pb), AdvSimd.AddSaturate(pa, pb));
            var pickA = AdvSimd.CompareLessThanOrEqual(pa, pb) & AdvSimd.CompareLessThanOrEqual(pa, pc);
            var pickB = AdvSimd.CompareLessThanOrEqual(pb, pc);
            return AdvSimd.BitwiseSelect(pickA, a, AdvSimd.BitwiseSelect(pickB, b, c)).AsVector();
        }

        var distanceA = Vector.Max(above, upperLeft) - Vector.Min(above, upperLeft);
        var distanceB = Vector.Max(left, upperLeft) - Vector.Min(left, upperLeft);

        // Same sign: b >= c and a >= c, or b <= c and a <= c (a zero difference makes both formulas equal)
        var aboveAtLeast = Vector.Equals(Vector.Max(above, upperLeft), above);
        var leftAtLeast = Vector.Equals(Vector.Max(left, upperLeft), left);
        var sum = distanceA + Vector.Min(distanceB, ~distanceA);
        var difference = Vector.Max(distanceA, distanceB) - Vector.Min(distanceA, distanceB);
        var distanceC = Vector.ConditionalSelect(aboveAtLeast ^ leftAtLeast, difference, sum);

        // x <= y is min(x, y) == x in unsigned byte lanes
        var useA = Vector.Equals(Vector.Min(distanceA, distanceB), distanceA) & Vector.Equals(Vector.Min(distanceA, distanceC), distanceA);
        var useB = Vector.Equals(Vector.Min(distanceB, distanceC), distanceB);
        return Vector.ConditionalSelect(useA, left, Vector.ConditionalSelect(useB, above, upperLeft));
    }

    /// <summary>The Paeth predictor of <see cref="Vector{T}.Count"/> bytes in 16-bit lanes: the reference of <see cref="PaethVector"/>.</summary>
    internal static Vector<byte> PaethVectorWide(Vector<byte> left, Vector<byte> above, Vector<byte> upperLeft)
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
