using System.Numerics;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The discrete wavelet transformations of T.800, Annex F: the reversible 5-3 filter on integers and the irreversible 9-7
/// filter on single-precision numbers, as the lifting steps of F.3.8 with the symmetric extension of F.3.7.
/// </summary>
/// <remarks>
/// <para>
/// The plane of a tile-component holds the sub-bands as the forward transformation leaves them (2D_DEINTERLEAVE): the
/// lower resolution level at the top left, the high-pass coefficients at its right and below it. A level is rebuilt in
/// place: each row is interleaved and filtered (HOR_SR), then each column (VER_SR), in that order (F.3.2).
/// </para>
/// <para>
/// A signal of one sample is not filtered: it is copied when its position is even and halved when it is odd (F.3.6).
/// </para>
/// <para>
/// The numerical contract of the irreversible filter: single precision, the constants of Table F.4 rounded once, each
/// lifting step computed as a sum, a product and a difference that are never fused, in the order of F.3.8.2. A column is
/// filtered like a row, so that a result does not depend on the width of the strips the columns are processed in.
/// </para>
/// <para>
/// A lifting step changes the samples of one parity from their two neighbors of the other parity, so that it is the same
/// operation on every sample of that parity: the vector paths apply it to several samples at once, on the rows of a strip
/// of columns and on the two halves of a row before they are interleaved (after they are separated, in the forward
/// direction). They compute what the scalar filters compute, bit for bit, in both directions and for both filters.
/// </para>
/// </remarks>
internal static class Jpeg2000Dwt
{
    /// <summary>The largest number of columns that are filtered together.</summary>
    public const int ColumnStrip = 64;

    // Table F.4
    private const float Alpha = -1.586134342059924f;
    private const float Beta = -0.052980118572961f;
    private const float Gamma = 0.882911075530934f;
    private const float Delta = 0.443506852043971f;
    private const float K = 1.230174104914001f;
    private const float InverseK = (float)(1 / 1.230174104914001);

    /// <summary>Gets the number of scratch elements the reconstruction of a tile-component needs.</summary>
    public static long GetScratchLength(int width, int height) => width + ((long)Math.Min(ColumnStrip, width) * height);

    /// <summary>Rebuilds the samples of a tile-component from its sub-bands, with the reversible filter.</summary>
    /// <param name="plane">The plane of the tile-component.</param>
    /// <param name="stride">The width of the plane.</param>
    /// <param name="resolutions">The resolution levels of the tile-component, from the lowest.</param>
    /// <param name="scratch">At least <see cref="GetScratchLength"/> elements.</param>
    public static void InverseReversible(Span<int> plane, int stride, ReadOnlySpan<Jpeg2000ResolutionGeometry> resolutions, Span<int> scratch)
        => InverseReversibleCore(plane, stride, resolutions, scratch, Vector.IsHardwareAccelerated);

    /// <summary>The scalar reference of <see cref="InverseReversible"/>.</summary>
    internal static void InverseReversibleScalar(Span<int> plane, int stride, ReadOnlySpan<Jpeg2000ResolutionGeometry> resolutions, Span<int> scratch)
        => InverseReversibleCore(plane, stride, resolutions, scratch, useVectors: false);

    private static void InverseReversibleCore(Span<int> plane, int stride, ReadOnlySpan<Jpeg2000ResolutionGeometry> resolutions, Span<int> scratch, bool useVectors)
    {
        for (var r = 1; r < resolutions.Length; r++)
        {
            ref readonly var lower = ref resolutions[r - 1];
            ref readonly var current = ref resolutions[r];
            var width = current.Width;
            var height = current.Height;
            if (width == 0 || height == 0)
                continue;

            var lowWidth = lower.Width;
            var lowHeight = lower.Height;
            var parityX = (int)(current.X0 & 1);
            var parityY = (int)(current.Y0 & 1);
            var line = scratch[..width];
            for (var y = 0; y < height; y++)
            {
                var row = plane.Slice(y * stride, width);
                if (useVectors && width > 1)
                {
                    LiftReversibleSeparated(row[..lowWidth], row[lowWidth..], parityX);
                    Interleave<int>(row, lowWidth, parityX, line, useVectors: true);
                }
                else
                {
                    Interleave<int>(row, lowWidth, parityX, line, useVectors: false);
                    LiftReversible(line, parityX);
                }

                line.CopyTo(row);
            }

            var strip = Math.Min(ColumnStrip, width);
            var columns = scratch.Slice(width, strip * height);
            for (var x = 0; x < width; x += strip)
            {
                var count = Math.Min(strip, width - x);
                GatherColumns(plane, stride, x, count, height, lowHeight, parityY, columns, strip);
                LiftReversibleColumns(columns, count, height, parityY, strip, useVectors);
                ScatterColumns(plane, stride, x, count, height, columns, strip);
            }
        }
    }

    /// <summary>Rebuilds the samples of a tile-component from its sub-bands, with the irreversible filter.</summary>
    /// <param name="plane">The plane of the tile-component.</param>
    /// <param name="stride">The width of the plane.</param>
    /// <param name="resolutions">The resolution levels of the tile-component, from the lowest.</param>
    /// <param name="scratch">At least <see cref="GetScratchLength"/> elements.</param>
    public static void InverseIrreversible(Span<float> plane, int stride, ReadOnlySpan<Jpeg2000ResolutionGeometry> resolutions, Span<float> scratch)
        => InverseIrreversibleCore(plane, stride, resolutions, scratch, Vector.IsHardwareAccelerated);

    /// <summary>The scalar reference of <see cref="InverseIrreversible"/>.</summary>
    internal static void InverseIrreversibleScalar(Span<float> plane, int stride, ReadOnlySpan<Jpeg2000ResolutionGeometry> resolutions, Span<float> scratch)
        => InverseIrreversibleCore(plane, stride, resolutions, scratch, useVectors: false);

    private static void InverseIrreversibleCore(Span<float> plane, int stride, ReadOnlySpan<Jpeg2000ResolutionGeometry> resolutions, Span<float> scratch, bool useVectors)
    {
        for (var r = 1; r < resolutions.Length; r++)
        {
            ref readonly var lower = ref resolutions[r - 1];
            ref readonly var current = ref resolutions[r];
            var width = current.Width;
            var height = current.Height;
            if (width == 0 || height == 0)
                continue;

            var lowWidth = lower.Width;
            var lowHeight = lower.Height;
            var parityX = (int)(current.X0 & 1);
            var parityY = (int)(current.Y0 & 1);
            var line = scratch[..width];
            for (var y = 0; y < height; y++)
            {
                var row = plane.Slice(y * stride, width);
                if (useVectors && width > 1)
                {
                    LiftIrreversibleSeparated(row[..lowWidth], row[lowWidth..], parityX);
                    Interleave<float>(row, lowWidth, parityX, line, useVectors: true);
                }
                else
                {
                    Interleave<float>(row, lowWidth, parityX, line, useVectors: false);
                    LiftIrreversible(line, parityX);
                }

                line.CopyTo(row);
            }

            var strip = Math.Min(ColumnStrip, width);
            var columns = scratch.Slice(width, strip * height);
            for (var x = 0; x < width; x += strip)
            {
                var count = Math.Min(strip, width - x);
                GatherColumns(plane, stride, x, count, height, lowHeight, parityY, columns, strip);
                LiftIrreversibleColumns(columns, count, height, parityY, strip, useVectors);
                ScatterColumns(plane, stride, x, count, height, columns, strip);
            }
        }
    }

    /// <summary>Decomposes the samples of a tile-component into its sub-bands, with the reversible filter (F.4).</summary>
    /// <param name="plane">The plane of the tile-component.</param>
    /// <param name="stride">The width of the plane.</param>
    /// <param name="resolutions">The resolution levels of the tile-component, from the lowest.</param>
    /// <param name="scratch">At least <see cref="GetScratchLength"/> elements.</param>
    public static void ForwardReversible(Span<int> plane, int stride, ReadOnlySpan<Jpeg2000ResolutionGeometry> resolutions, Span<int> scratch)
        => ForwardReversibleCore(plane, stride, resolutions, scratch, Vector.IsHardwareAccelerated);

    /// <summary>The scalar reference of <see cref="ForwardReversible"/>.</summary>
    internal static void ForwardReversibleScalar(Span<int> plane, int stride, ReadOnlySpan<Jpeg2000ResolutionGeometry> resolutions, Span<int> scratch)
        => ForwardReversibleCore(plane, stride, resolutions, scratch, useVectors: false);

    private static void ForwardReversibleCore(Span<int> plane, int stride, ReadOnlySpan<Jpeg2000ResolutionGeometry> resolutions, Span<int> scratch, bool useVectors)
    {
        for (var r = resolutions.Length - 1; r >= 1; r--)
        {
            ref readonly var lower = ref resolutions[r - 1];
            ref readonly var current = ref resolutions[r];
            var width = current.Width;
            var height = current.Height;
            if (width == 0 || height == 0)
                continue;

            var lowWidth = lower.Width;
            var lowHeight = lower.Height;
            var parityX = (int)(current.X0 & 1);
            var parityY = (int)(current.Y0 & 1);

            // 2D_SD (F.4.2): the columns (VER_SD), then the rows (HOR_SD)
            var strip = Math.Min(ColumnStrip, width);
            var columns = scratch.Slice(width, strip * height);
            for (var x = 0; x < width; x += strip)
            {
                var count = Math.Min(strip, width - x);
                GatherRows(plane, stride, x, count, height, columns, strip);
                LiftForwardReversibleColumns(columns, count, height, parityY, strip, useVectors);
                ScatterColumnsDeinterleaved(plane, stride, x, count, height, lowHeight, parityY, columns, strip);
            }

            var line = scratch[..width];
            for (var y = 0; y < height; y++)
            {
                var row = plane.Slice(y * stride, width);
                if (useVectors && width > 1)
                {
                    Deinterleave<int>(row, lowWidth, parityX, line, useVectors: true);
                    LiftForwardReversibleSeparated(line[..lowWidth], line[lowWidth..], parityX);
                    line.CopyTo(row);
                }
                else
                {
                    row.CopyTo(line);
                    LiftForwardReversible(line, parityX);
                    Deinterleave<int>(line, lowWidth, parityX, row, useVectors: false);
                }
            }
        }
    }

    /// <summary>Decomposes the samples of a tile-component into its sub-bands, with the irreversible filter (F.4).</summary>
    /// <param name="plane">The plane of the tile-component.</param>
    /// <param name="stride">The width of the plane.</param>
    /// <param name="resolutions">The resolution levels of the tile-component, from the lowest.</param>
    /// <param name="scratch">At least <see cref="GetScratchLength"/> elements.</param>
    public static void ForwardIrreversible(Span<float> plane, int stride, ReadOnlySpan<Jpeg2000ResolutionGeometry> resolutions, Span<float> scratch)
        => ForwardIrreversibleCore(plane, stride, resolutions, scratch, Vector.IsHardwareAccelerated);

    /// <summary>The scalar reference of <see cref="ForwardIrreversible"/>.</summary>
    internal static void ForwardIrreversibleScalar(Span<float> plane, int stride, ReadOnlySpan<Jpeg2000ResolutionGeometry> resolutions, Span<float> scratch)
        => ForwardIrreversibleCore(plane, stride, resolutions, scratch, useVectors: false);

    private static void ForwardIrreversibleCore(Span<float> plane, int stride, ReadOnlySpan<Jpeg2000ResolutionGeometry> resolutions, Span<float> scratch, bool useVectors)
    {
        for (var r = resolutions.Length - 1; r >= 1; r--)
        {
            ref readonly var lower = ref resolutions[r - 1];
            ref readonly var current = ref resolutions[r];
            var width = current.Width;
            var height = current.Height;
            if (width == 0 || height == 0)
                continue;

            var lowWidth = lower.Width;
            var lowHeight = lower.Height;
            var parityX = (int)(current.X0 & 1);
            var parityY = (int)(current.Y0 & 1);
            var strip = Math.Min(ColumnStrip, width);
            var columns = scratch.Slice(width, strip * height);
            for (var x = 0; x < width; x += strip)
            {
                var count = Math.Min(strip, width - x);
                GatherRows(plane, stride, x, count, height, columns, strip);
                LiftForwardIrreversibleColumns(columns, count, height, parityY, strip, useVectors);
                ScatterColumnsDeinterleaved(plane, stride, x, count, height, lowHeight, parityY, columns, strip);
            }

            var line = scratch[..width];
            for (var y = 0; y < height; y++)
            {
                var row = plane.Slice(y * stride, width);
                if (useVectors && width > 1)
                {
                    Deinterleave<float>(row, lowWidth, parityX, line, useVectors: true);
                    LiftForwardIrreversibleSeparated(line[..lowWidth], line[lowWidth..], parityX);
                    line.CopyTo(row);
                }
                else
                {
                    row.CopyTo(line);
                    LiftForwardIrreversible(line, parityX);
                    Deinterleave<float>(line, lowWidth, parityX, row, useVectors: false);
                }
            }
        }
    }

    /// <summary>The forward reversible 1D filter (1D_FILTD, F.4.8.1) on a signal whose first sample has the parity <paramref name="parity"/>.</summary>
    internal static void LiftForwardReversible(Span<int> signal, int parity)
    {
        var length = signal.Length;
        if (length == 1)
        {
            if (parity != 0)
            {
                signal[0] <<= 1;
            }

            return;
        }

        // Equation F-9: the samples at odd positions
        for (var i = 1 - parity; i < length; i += 2)
        {
            signal[i] -= (signal[Mirror(i - 1, length)] + signal[Mirror(i + 1, length)]) >> 1;
        }

        // Equation F-10: the samples at even positions
        for (var i = parity; i < length; i += 2)
        {
            signal[i] += (signal[Mirror(i - 1, length)] + signal[Mirror(i + 1, length)] + 2) >> 2;
        }
    }

    /// <summary>The forward irreversible 1D filter (1D_FILTD, F.4.8.2) on a signal whose first sample has the parity <paramref name="parity"/>.</summary>
    internal static void LiftForwardIrreversible(Span<float> signal, int parity)
    {
        var length = signal.Length;
        if (length == 1)
        {
            if (parity != 0)
            {
                signal[0] *= 2f;
            }

            return;
        }

        var even = parity;
        var odd = 1 - parity;
        LiftStep(signal, odd, Alpha);
        LiftStep(signal, even, Beta);
        LiftStep(signal, odd, Gamma);
        LiftStep(signal, even, Delta);
        for (var i = odd; i < length; i += 2)
        {
            signal[i] *= K;
        }

        for (var i = even; i < length; i += 2)
        {
            signal[i] *= InverseK;
        }

        static void LiftStep(Span<float> signal, int start, float factor)
        {
            var length = signal.Length;
            for (var i = start; i < length; i += 2)
            {
                var sum = signal[Mirror(i - 1, length)] + signal[Mirror(i + 1, length)];
                var product = factor * sum;
                signal[i] += product;
            }
        }
    }

    /// <summary>The reversible 1D filter (1D_FILTR, F.3.8.1) on an interleaved signal whose first sample has the parity <paramref name="parity"/>.</summary>
    internal static void LiftReversible(Span<int> signal, int parity)
    {
        var length = signal.Length;
        if (length == 1)
        {
            if (parity != 0)
            {
                signal[0] >>= 1;
            }

            return;
        }

        // Equation F-5: the samples at even positions
        for (var i = parity; i < length; i += 2)
        {
            signal[i] -= (signal[Mirror(i - 1, length)] + signal[Mirror(i + 1, length)] + 2) >> 2;
        }

        // Equation F-6: the samples at odd positions
        for (var i = 1 - parity; i < length; i += 2)
        {
            signal[i] += (signal[Mirror(i - 1, length)] + signal[Mirror(i + 1, length)]) >> 1;
        }
    }

    /// <summary>The irreversible 1D filter (1D_FILTR, F.3.8.2) on an interleaved signal whose first sample has the parity <paramref name="parity"/>.</summary>
    internal static void LiftIrreversible(Span<float> signal, int parity)
    {
        var length = signal.Length;
        if (length == 1)
        {
            if (parity != 0)
            {
                signal[0] *= 0.5f;
            }

            return;
        }

        var even = parity;
        var odd = 1 - parity;
        for (var i = even; i < length; i += 2)
        {
            signal[i] *= K;
        }

        for (var i = odd; i < length; i += 2)
        {
            signal[i] *= InverseK;
        }

        LiftStep(signal, even, Delta);
        LiftStep(signal, odd, Gamma);
        LiftStep(signal, even, Beta);
        LiftStep(signal, odd, Alpha);

        static void LiftStep(Span<float> signal, int start, float factor)
        {
            var length = signal.Length;
            for (var i = start; i < length; i += 2)
            {
                var sum = signal[Mirror(i - 1, length)] + signal[Mirror(i + 1, length)];
                var product = factor * sum;
                signal[i] -= product;
            }
        }
    }

    /// <summary>The periodic symmetric extension of F.3.7, for a signal of at least two samples and an index one sample out of it.</summary>
    private static int Mirror(int index, int length)
    {
        if (index < 0)
            return -index;

        return index >= length ? (2 * (length - 1)) - index : index;
    }

    /// <summary>1D_INTERLEAVE (F.3.5): the low-pass samples go to the even positions and the high-pass samples to the odd ones.</summary>
    private static void Interleave<T>(ReadOnlySpan<T> row, int lowCount, int parity, Span<T> line, bool useVectors)
    {
        var low = row[..lowCount];
        var high = row[lowCount..];
        var i = 0;
        if (useVectors && BitConverter.IsLittleEndian && Vector<T>.Count == Vector<uint>.Count)
        {
            // Two samples of 32 bits that follow each other are the two halves of a 64-bit element, the first one in its low
            // half. The row starts with a sample of "first", which has as many samples as "second" or one more
            var first = parity == 0 ? low : high;
            var second = parity == 0 ? high : low;
            for (; i <= second.Length - Vector<T>.Count; i += Vector<T>.Count)
            {
                Vector.Widen(Vector.AsVectorUInt32(new Vector<T>(first[i..])), out var firstStart, out var firstEnd);
                Vector.Widen(Vector.AsVectorUInt32(new Vector<T>(second[i..])), out var secondStart, out var secondEnd);
                Vector.As<ulong, T>(firstStart | (secondStart << 32)).CopyTo(line[(2 * i)..]);
                Vector.As<ulong, T>(firstEnd | (secondEnd << 32)).CopyTo(line[((2 * i) + Vector<T>.Count)..]);
            }
        }

        for (var j = i; j < low.Length; j++)
        {
            line[(2 * j) + parity] = low[j];
        }

        for (var j = i; j < high.Length; j++)
        {
            line[(2 * j) + 1 - parity] = high[j];
        }
    }

    /// <summary>Copies a strip of columns, row by row, with the rows interleaved.</summary>
    private static void GatherColumns<T>(ReadOnlySpan<T> plane, int stride, int x, int count, int height, int lowCount, int parity, Span<T> columns, int strip)
    {
        for (var y = 0; y < height; y++)
        {
            var isLow = ((y + parity) & 1) == 0;
            var source = isLow ? y >> 1 : lowCount + (y >> 1);
            plane.Slice((source * stride) + x, count).CopyTo(columns.Slice(y * strip, count));
        }
    }

    private static void ScatterColumns<T>(Span<T> plane, int stride, int x, int count, int height, ReadOnlySpan<T> columns, int strip)
    {
        for (var y = 0; y < height; y++)
        {
            columns.Slice(y * strip, count).CopyTo(plane.Slice((y * stride) + x, count));
        }
    }

    /// <summary>1D_DEINTERLEAVE (F.4.5): the samples at even positions go to the front and the samples at odd positions after them.</summary>
    private static void Deinterleave<T>(ReadOnlySpan<T> line, int lowCount, int parity, Span<T> row, bool useVectors)
    {
        var low = row[..lowCount];
        var high = row[lowCount..];
        var i = 0;
        if (useVectors && BitConverter.IsLittleEndian && Vector<T>.Count == Vector<uint>.Count)
        {
            // The reverse of Interleave: the low halves of the 64-bit elements are the samples of "first"
            var first = parity == 0 ? low : high;
            var second = parity == 0 ? high : low;
            for (; i <= second.Length - Vector<T>.Count; i += Vector<T>.Count)
            {
                var start = Vector.AsVectorUInt64(new Vector<T>(line[(2 * i)..]));
                var end = Vector.AsVectorUInt64(new Vector<T>(line[((2 * i) + Vector<T>.Count)..]));
                Vector.As<uint, T>(Vector.Narrow(start, end)).CopyTo(first[i..]);
                Vector.As<uint, T>(Vector.Narrow(start >> 32, end >> 32)).CopyTo(second[i..]);
            }
        }

        for (var j = i; j < low.Length; j++)
        {
            low[j] = line[(2 * j) + parity];
        }

        for (var j = i; j < high.Length; j++)
        {
            high[j] = line[(2 * j) + 1 - parity];
        }
    }

    /// <summary>Copies a strip of columns, row by row.</summary>
    private static void GatherRows<T>(ReadOnlySpan<T> plane, int stride, int x, int count, int height, Span<T> columns, int strip)
    {
        for (var y = 0; y < height; y++)
        {
            plane.Slice((y * stride) + x, count).CopyTo(columns.Slice(y * strip, count));
        }
    }

    /// <summary>Writes a strip of columns back with its rows deinterleaved: the rows at even positions first.</summary>
    private static void ScatterColumnsDeinterleaved<T>(Span<T> plane, int stride, int x, int count, int height, int lowCount, int parity, ReadOnlySpan<T> columns, int strip)
    {
        for (var y = 0; y < height; y++)
        {
            var isLow = ((y + parity) & 1) == 0;
            var destination = isLow ? y >> 1 : lowCount + (y >> 1);
            columns.Slice(y * strip, count).CopyTo(plane.Slice((destination * stride) + x, count));
        }
    }

    private static void LiftForwardReversibleColumns(Span<int> columns, int count, int height, int parity, int strip, bool useVectors)
    {
        if (height == 1)
        {
            if (parity != 0)
            {
                foreach (ref var value in columns[..count])
                {
                    value <<= 1;
                }
            }

            return;
        }

        for (var y = 1 - parity; y < height; y += 2)
        {
            var previous = columns.Slice(Mirror(y - 1, height) * strip, count);
            var next = columns.Slice(Mirror(y + 1, height) * strip, count);
            Lift(columns.Slice(y * strip, count), previous, next, bias: 0, shift: 1, subtract: true, useVectors);
        }

        for (var y = parity; y < height; y += 2)
        {
            var previous = columns.Slice(Mirror(y - 1, height) * strip, count);
            var next = columns.Slice(Mirror(y + 1, height) * strip, count);
            Lift(columns.Slice(y * strip, count), previous, next, bias: 2, shift: 2, subtract: false, useVectors);
        }
    }

    private static void LiftForwardIrreversibleColumns(Span<float> columns, int count, int height, int parity, int strip, bool useVectors)
    {
        if (height == 1)
        {
            if (parity != 0)
            {
                foreach (ref var value in columns[..count])
                {
                    value *= 2f;
                }
            }

            return;
        }

        var even = parity;
        var odd = 1 - parity;
        LiftStep(columns, count, height, strip, odd, Alpha, useVectors);
        LiftStep(columns, count, height, strip, even, Beta, useVectors);
        LiftStep(columns, count, height, strip, odd, Gamma, useVectors);
        LiftStep(columns, count, height, strip, even, Delta, useVectors);
        for (var y = 0; y < height; y++)
        {
            Scale(columns.Slice(y * strip, count), ((y + parity) & 1) == 0 ? InverseK : K, useVectors);
        }

        static void LiftStep(Span<float> columns, int count, int height, int strip, int start, float factor, bool useVectors)
        {
            for (var y = start; y < height; y += 2)
            {
                var previous = columns.Slice(Mirror(y - 1, height) * strip, count);
                var next = columns.Slice(Mirror(y + 1, height) * strip, count);
                Lift(columns.Slice(y * strip, count), previous, next, factor, subtract: false, useVectors);
            }
        }
    }

    private static void LiftReversibleColumns(Span<int> columns, int count, int height, int parity, int strip, bool useVectors)
    {
        if (height == 1)
        {
            if (parity != 0)
            {
                foreach (ref var value in columns[..count])
                {
                    value >>= 1;
                }
            }

            return;
        }

        for (var y = parity; y < height; y += 2)
        {
            var previous = columns.Slice(Mirror(y - 1, height) * strip, count);
            var next = columns.Slice(Mirror(y + 1, height) * strip, count);
            Lift(columns.Slice(y * strip, count), previous, next, bias: 2, shift: 2, subtract: true, useVectors);
        }

        for (var y = 1 - parity; y < height; y += 2)
        {
            var previous = columns.Slice(Mirror(y - 1, height) * strip, count);
            var next = columns.Slice(Mirror(y + 1, height) * strip, count);
            Lift(columns.Slice(y * strip, count), previous, next, bias: 0, shift: 1, subtract: false, useVectors);
        }
    }

    private static void LiftIrreversibleColumns(Span<float> columns, int count, int height, int parity, int strip, bool useVectors)
    {
        if (height == 1)
        {
            if (parity != 0)
            {
                foreach (ref var value in columns[..count])
                {
                    value *= 0.5f;
                }
            }

            return;
        }

        var even = parity;
        var odd = 1 - parity;
        for (var y = 0; y < height; y++)
        {
            Scale(columns.Slice(y * strip, count), ((y + parity) & 1) == 0 ? K : InverseK, useVectors);
        }

        LiftStep(columns, count, height, strip, even, Delta, useVectors);
        LiftStep(columns, count, height, strip, odd, Gamma, useVectors);
        LiftStep(columns, count, height, strip, even, Beta, useVectors);
        LiftStep(columns, count, height, strip, odd, Alpha, useVectors);

        static void LiftStep(Span<float> columns, int count, int height, int strip, int start, float factor, bool useVectors)
        {
            for (var y = start; y < height; y += 2)
            {
                var previous = columns.Slice(Mirror(y - 1, height) * strip, count);
                var next = columns.Slice(Mirror(y + 1, height) * strip, count);
                Lift(columns.Slice(y * strip, count), previous, next, factor, subtract: true, useVectors);
            }
        }
    }

    /// <summary>The reversible filter on a row whose halves are not interleaved yet: what <see cref="LiftReversible"/> does to the interleaved row.</summary>
    private static void LiftReversibleSeparated(Span<int> low, Span<int> high, int parity)
    {
        LiftHalf(low, high, parity, bias: 2, shift: 2, subtract: true);
        LiftHalf(high, low, 1 - parity, bias: 0, shift: 1, subtract: false);
    }

    /// <summary>The forward reversible filter on a row whose halves are separated already: what <see cref="LiftForwardReversible"/> does to the row.</summary>
    private static void LiftForwardReversibleSeparated(Span<int> low, Span<int> high, int parity)
    {
        LiftHalf(high, low, 1 - parity, bias: 0, shift: 1, subtract: true);
        LiftHalf(low, high, parity, bias: 2, shift: 2, subtract: false);
    }

    /// <summary>The irreversible filter on a row whose halves are not interleaved yet: what <see cref="LiftIrreversible"/> does to the interleaved row.</summary>
    private static void LiftIrreversibleSeparated(Span<float> low, Span<float> high, int parity)
    {
        Scale(low, K, useVectors: true);
        Scale(high, InverseK, useVectors: true);
        LiftHalf(low, high, parity, Delta, subtract: true);
        LiftHalf(high, low, 1 - parity, Gamma, subtract: true);
        LiftHalf(low, high, parity, Beta, subtract: true);
        LiftHalf(high, low, 1 - parity, Alpha, subtract: true);
    }

    /// <summary>The forward irreversible filter on a row whose halves are separated already: what <see cref="LiftForwardIrreversible"/> does to the row.</summary>
    private static void LiftForwardIrreversibleSeparated(Span<float> low, Span<float> high, int parity)
    {
        LiftHalf(high, low, 1 - parity, Alpha, subtract: false);
        LiftHalf(low, high, parity, Beta, subtract: false);
        LiftHalf(high, low, 1 - parity, Gamma, subtract: false);
        LiftHalf(low, high, parity, Delta, subtract: false);
        Scale(high, K, useVectors: true);
        Scale(low, InverseK, useVectors: true);
    }

    /// <summary>
    /// A lifting step of the reversible filter on one half of a row of at least two samples. The sample
    /// <c>target[i]</c> lies between <c>source[i + offset - 1]</c> and <c>source[i + offset]</c>; at an end of the row, the
    /// neighbor that does not exist is the other one (the symmetric extension of F.3.7).
    /// </summary>
    /// <param name="target">The half that changes.</param>
    /// <param name="source">The other half.</param>
    /// <param name="offset">1 when the row starts with a sample of <paramref name="source"/>; otherwise, 0.</param>
    /// <param name="bias">The rounding term of the step.</param>
    /// <param name="shift">The shift of the step.</param>
    /// <param name="subtract"><see langword="true"/> to subtract the term from the samples; <see langword="false"/> to add it.</param>
    private static void LiftHalf(Span<int> target, ReadOnlySpan<int> source, int offset, int bias, int shift, bool subtract)
    {
        var start = 1 - offset;
        var end = Math.Min(target.Length, source.Length - offset);
        if (start > 0)
        {
            Lift(target[..1], source[..1], source[..1], bias, shift, subtract, useVectors: false);
        }

        Lift(target[start..end], source[..(end - start)], source.Slice(1, end - start), bias, shift, subtract, useVectors: true);
        if (end < target.Length)
        {
            var last = source.Slice(end + offset - 1, 1);
            Lift(target.Slice(end, 1), last, last, bias, shift, subtract, useVectors: false);
        }
    }

    /// <summary>A lifting step of the irreversible filter on one half of a row of at least two samples.</summary>
    /// <param name="target">The half that changes.</param>
    /// <param name="source">The other half.</param>
    /// <param name="offset">1 when the row starts with a sample of <paramref name="source"/>; otherwise, 0.</param>
    /// <param name="factor">The lifting parameter of the step.</param>
    /// <param name="subtract"><see langword="true"/> to subtract the term from the samples; <see langword="false"/> to add it.</param>
    private static void LiftHalf(Span<float> target, ReadOnlySpan<float> source, int offset, float factor, bool subtract)
    {
        var start = 1 - offset;
        var end = Math.Min(target.Length, source.Length - offset);
        if (start > 0)
        {
            Lift(target[..1], source[..1], source[..1], factor, subtract, useVectors: false);
        }

        Lift(target[start..end], source[..(end - start)], source.Slice(1, end - start), factor, subtract, useVectors: true);
        if (end < target.Length)
        {
            var last = source.Slice(end + offset - 1, 1);
            Lift(target.Slice(end, 1), last, last, factor, subtract, useVectors: false);
        }
    }

    /// <summary>A lifting step of the reversible filter: <c>target[i] -= (first[i] + second[i] + bias) >> shift</c>, or the addition.</summary>
    private static void Lift(Span<int> target, ReadOnlySpan<int> first, ReadOnlySpan<int> second, int bias, int shift, bool subtract, bool useVectors)
    {
        // The vectors are read without a bound check: the three spans have the length of the target
        first = first[..target.Length];
        second = second[..target.Length];
        var i = 0;
        if (useVectors)
        {
            ref var targetStart = ref unsafe(MemoryMarshal.GetReference(target));
            ref var firstStart = ref unsafe(MemoryMarshal.GetReference(first));
            ref var secondStart = ref unsafe(MemoryMarshal.GetReference(second));
            var biases = new Vector<int>(bias);
            for (; i <= target.Length - Vector<int>.Count; i += Vector<int>.Count)
            {
                var term = (unsafe(Vector.LoadUnsafe(ref firstStart, (nuint)i)) + unsafe(Vector.LoadUnsafe(ref secondStart, (nuint)i)) + biases) >> shift;
                var value = unsafe(Vector.LoadUnsafe(ref targetStart, (nuint)i));
                unsafe { (subtract ? value - term : value + term).StoreUnsafe(ref targetStart, (nuint)i); }
            }
        }

        for (; i < target.Length; i++)
        {
            var term = (first[i] + second[i] + bias) >> shift;
            target[i] = subtract ? target[i] - term : target[i] + term;
        }
    }

    /// <summary>A lifting step of the irreversible filter: <c>target[i] -= factor * (first[i] + second[i])</c>, or the addition.</summary>
    private static void Lift(Span<float> target, ReadOnlySpan<float> first, ReadOnlySpan<float> second, float factor, bool subtract, bool useVectors)
    {
        // The vectors are read without a bound check: the three spans have the length of the target
        first = first[..target.Length];
        second = second[..target.Length];
        var i = 0;
        if (useVectors)
        {
            // The sum, the product and the difference are three operations, like the scalar ones: nothing is fused
            ref var targetStart = ref unsafe(MemoryMarshal.GetReference(target));
            ref var firstStart = ref unsafe(MemoryMarshal.GetReference(first));
            ref var secondStart = ref unsafe(MemoryMarshal.GetReference(second));
            var factors = new Vector<float>(factor);
            for (; i <= target.Length - Vector<float>.Count; i += Vector<float>.Count)
            {
                var sum = unsafe(Vector.LoadUnsafe(ref firstStart, (nuint)i)) + unsafe(Vector.LoadUnsafe(ref secondStart, (nuint)i));
                var product = factors * sum;
                var value = unsafe(Vector.LoadUnsafe(ref targetStart, (nuint)i));
                unsafe { (subtract ? value - product : value + product).StoreUnsafe(ref targetStart, (nuint)i); }
            }
        }

        for (; i < target.Length; i++)
        {
            var sum = first[i] + second[i];
            var product = factor * sum;
            target[i] = subtract ? target[i] - product : target[i] + product;
        }
    }

    /// <summary>The scaling step of the irreversible filter: <c>values[i] *= factor</c>.</summary>
    private static void Scale(Span<float> values, float factor, bool useVectors)
    {
        var i = 0;
        if (useVectors)
        {
            var factors = new Vector<float>(factor);
            for (; i <= values.Length - Vector<float>.Count; i += Vector<float>.Count)
            {
                (new Vector<float>(values[i..]) * factors).CopyTo(values[i..]);
            }
        }

        for (; i < values.Length; i++)
        {
            values[i] *= factor;
        }
    }
}
