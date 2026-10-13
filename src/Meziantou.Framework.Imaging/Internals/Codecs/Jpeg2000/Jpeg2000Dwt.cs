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
/// </remarks>
internal static class Jpeg2000Dwt
{
    /// <summary>The number of columns that are filtered together.</summary>
    public const int ColumnStrip = 16;

    // Table F.4
    private const float Alpha = -1.586134342059924f;
    private const float Beta = -0.052980118572961f;
    private const float Gamma = 0.882911075530934f;
    private const float Delta = 0.443506852043971f;
    private const float K = 1.230174104914001f;
    private const float InverseK = (float)(1 / 1.230174104914001);

    /// <summary>Gets the number of scratch elements the reconstruction of a tile-component needs.</summary>
    public static long GetScratchLength(int width, int height) => width + ((long)ColumnStrip * height);

    /// <summary>Rebuilds the samples of a tile-component from its sub-bands, with the reversible filter.</summary>
    /// <param name="plane">The plane of the tile-component.</param>
    /// <param name="stride">The width of the plane.</param>
    /// <param name="resolutions">The resolution levels of the tile-component, from the lowest.</param>
    /// <param name="scratch">At least <see cref="GetScratchLength"/> elements.</param>
    public static void InverseReversible(Span<int> plane, int stride, ReadOnlySpan<Jpeg2000ResolutionGeometry> resolutions, Span<int> scratch)
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
                Interleave(row, lowWidth, parityX, line);
                LiftReversible(line, parityX);
                line.CopyTo(row);
            }

            var columns = scratch.Slice(width, ColumnStrip * height);
            for (var x = 0; x < width; x += ColumnStrip)
            {
                var count = Math.Min(ColumnStrip, width - x);
                GatherColumns(plane, stride, x, count, height, lowHeight, parityY, columns);
                LiftReversibleColumns(columns, count, height, parityY);
                ScatterColumns(plane, stride, x, count, height, columns);
            }
        }
    }

    /// <summary>Rebuilds the samples of a tile-component from its sub-bands, with the irreversible filter.</summary>
    /// <param name="plane">The plane of the tile-component.</param>
    /// <param name="stride">The width of the plane.</param>
    /// <param name="resolutions">The resolution levels of the tile-component, from the lowest.</param>
    /// <param name="scratch">At least <see cref="GetScratchLength"/> elements.</param>
    public static void InverseIrreversible(Span<float> plane, int stride, ReadOnlySpan<Jpeg2000ResolutionGeometry> resolutions, Span<float> scratch)
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
                Interleave(row, lowWidth, parityX, line);
                LiftIrreversible(line, parityX);
                line.CopyTo(row);
            }

            var columns = scratch.Slice(width, ColumnStrip * height);
            for (var x = 0; x < width; x += ColumnStrip)
            {
                var count = Math.Min(ColumnStrip, width - x);
                GatherColumns(plane, stride, x, count, height, lowHeight, parityY, columns);
                LiftIrreversibleColumns(columns, count, height, parityY);
                ScatterColumns(plane, stride, x, count, height, columns);
            }
        }
    }

    /// <summary>Decomposes the samples of a tile-component into its sub-bands, with the reversible filter (F.4).</summary>
    /// <param name="plane">The plane of the tile-component.</param>
    /// <param name="stride">The width of the plane.</param>
    /// <param name="resolutions">The resolution levels of the tile-component, from the lowest.</param>
    /// <param name="scratch">At least <see cref="GetScratchLength"/> elements.</param>
    public static void ForwardReversible(Span<int> plane, int stride, ReadOnlySpan<Jpeg2000ResolutionGeometry> resolutions, Span<int> scratch)
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
            var columns = scratch.Slice(width, ColumnStrip * height);
            for (var x = 0; x < width; x += ColumnStrip)
            {
                var count = Math.Min(ColumnStrip, width - x);
                GatherRows(plane, stride, x, count, height, columns);
                LiftForwardReversibleColumns(columns, count, height, parityY);
                ScatterColumnsDeinterleaved(plane, stride, x, count, height, lowHeight, parityY, columns);
            }

            var line = scratch[..width];
            for (var y = 0; y < height; y++)
            {
                var row = plane.Slice(y * stride, width);
                row.CopyTo(line);
                LiftForwardReversible(line, parityX);
                Deinterleave(line, lowWidth, parityX, row);
            }
        }
    }

    /// <summary>Decomposes the samples of a tile-component into its sub-bands, with the irreversible filter (F.4).</summary>
    /// <param name="plane">The plane of the tile-component.</param>
    /// <param name="stride">The width of the plane.</param>
    /// <param name="resolutions">The resolution levels of the tile-component, from the lowest.</param>
    /// <param name="scratch">At least <see cref="GetScratchLength"/> elements.</param>
    public static void ForwardIrreversible(Span<float> plane, int stride, ReadOnlySpan<Jpeg2000ResolutionGeometry> resolutions, Span<float> scratch)
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
            var columns = scratch.Slice(width, ColumnStrip * height);
            for (var x = 0; x < width; x += ColumnStrip)
            {
                var count = Math.Min(ColumnStrip, width - x);
                GatherRows(plane, stride, x, count, height, columns);
                LiftForwardIrreversibleColumns(columns, count, height, parityY);
                ScatterColumnsDeinterleaved(plane, stride, x, count, height, lowHeight, parityY, columns);
            }

            var line = scratch[..width];
            for (var y = 0; y < height; y++)
            {
                var row = plane.Slice(y * stride, width);
                row.CopyTo(line);
                LiftForwardIrreversible(line, parityX);
                Deinterleave(line, lowWidth, parityX, row);
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
    private static void Interleave<T>(ReadOnlySpan<T> row, int lowCount, int parity, Span<T> line)
    {
        var low = row[..lowCount];
        var high = row[lowCount..];
        for (var i = 0; i < low.Length; i++)
        {
            line[(2 * i) + parity] = low[i];
        }

        for (var i = 0; i < high.Length; i++)
        {
            line[(2 * i) + 1 - parity] = high[i];
        }
    }

    /// <summary>Copies a strip of columns, row by row, with the rows interleaved.</summary>
    private static void GatherColumns<T>(ReadOnlySpan<T> plane, int stride, int x, int count, int height, int lowCount, int parity, Span<T> columns)
    {
        for (var y = 0; y < height; y++)
        {
            var isLow = ((y + parity) & 1) == 0;
            var source = isLow ? y >> 1 : lowCount + (y >> 1);
            plane.Slice((source * stride) + x, count).CopyTo(columns.Slice(y * ColumnStrip, count));
        }
    }

    private static void ScatterColumns<T>(Span<T> plane, int stride, int x, int count, int height, ReadOnlySpan<T> columns)
    {
        for (var y = 0; y < height; y++)
        {
            columns.Slice(y * ColumnStrip, count).CopyTo(plane.Slice((y * stride) + x, count));
        }
    }

    /// <summary>1D_DEINTERLEAVE (F.4.5): the samples at even positions go to the front and the samples at odd positions after them.</summary>
    private static void Deinterleave<T>(ReadOnlySpan<T> line, int lowCount, int parity, Span<T> row)
    {
        var low = row[..lowCount];
        var high = row[lowCount..];
        for (var i = 0; i < low.Length; i++)
        {
            low[i] = line[(2 * i) + parity];
        }

        for (var i = 0; i < high.Length; i++)
        {
            high[i] = line[(2 * i) + 1 - parity];
        }
    }

    /// <summary>Copies a strip of columns, row by row.</summary>
    private static void GatherRows<T>(ReadOnlySpan<T> plane, int stride, int x, int count, int height, Span<T> columns)
    {
        for (var y = 0; y < height; y++)
        {
            plane.Slice((y * stride) + x, count).CopyTo(columns.Slice(y * ColumnStrip, count));
        }
    }

    /// <summary>Writes a strip of columns back with its rows deinterleaved: the rows at even positions first.</summary>
    private static void ScatterColumnsDeinterleaved<T>(Span<T> plane, int stride, int x, int count, int height, int lowCount, int parity, ReadOnlySpan<T> columns)
    {
        for (var y = 0; y < height; y++)
        {
            var isLow = ((y + parity) & 1) == 0;
            var destination = isLow ? y >> 1 : lowCount + (y >> 1);
            columns.Slice(y * ColumnStrip, count).CopyTo(plane.Slice((destination * stride) + x, count));
        }
    }

    private static void LiftForwardReversibleColumns(Span<int> columns, int count, int height, int parity)
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
            var previous = columns.Slice(Mirror(y - 1, height) * ColumnStrip, count);
            var next = columns.Slice(Mirror(y + 1, height) * ColumnStrip, count);
            var row = columns.Slice(y * ColumnStrip, count);
            for (var i = 0; i < row.Length; i++)
            {
                row[i] -= (previous[i] + next[i]) >> 1;
            }
        }

        for (var y = parity; y < height; y += 2)
        {
            var previous = columns.Slice(Mirror(y - 1, height) * ColumnStrip, count);
            var next = columns.Slice(Mirror(y + 1, height) * ColumnStrip, count);
            var row = columns.Slice(y * ColumnStrip, count);
            for (var i = 0; i < row.Length; i++)
            {
                row[i] += (previous[i] + next[i] + 2) >> 2;
            }
        }
    }

    private static void LiftForwardIrreversibleColumns(Span<float> columns, int count, int height, int parity)
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
        LiftStep(columns, count, height, odd, Alpha);
        LiftStep(columns, count, height, even, Beta);
        LiftStep(columns, count, height, odd, Gamma);
        LiftStep(columns, count, height, even, Delta);
        for (var y = 0; y < height; y++)
        {
            var factor = ((y + parity) & 1) == 0 ? InverseK : K;
            foreach (ref var value in columns.Slice(y * ColumnStrip, count))
            {
                value *= factor;
            }
        }

        static void LiftStep(Span<float> columns, int count, int height, int start, float factor)
        {
            for (var y = start; y < height; y += 2)
            {
                var previous = columns.Slice(Mirror(y - 1, height) * ColumnStrip, count);
                var next = columns.Slice(Mirror(y + 1, height) * ColumnStrip, count);
                var row = columns.Slice(y * ColumnStrip, count);
                for (var i = 0; i < row.Length; i++)
                {
                    var sum = previous[i] + next[i];
                    var product = factor * sum;
                    row[i] += product;
                }
            }
        }
    }

    private static void LiftReversibleColumns(Span<int> columns, int count, int height, int parity)
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
            var previous = columns.Slice(Mirror(y - 1, height) * ColumnStrip, count);
            var next = columns.Slice(Mirror(y + 1, height) * ColumnStrip, count);
            var row = columns.Slice(y * ColumnStrip, count);
            for (var i = 0; i < row.Length; i++)
            {
                row[i] -= (previous[i] + next[i] + 2) >> 2;
            }
        }

        for (var y = 1 - parity; y < height; y += 2)
        {
            var previous = columns.Slice(Mirror(y - 1, height) * ColumnStrip, count);
            var next = columns.Slice(Mirror(y + 1, height) * ColumnStrip, count);
            var row = columns.Slice(y * ColumnStrip, count);
            for (var i = 0; i < row.Length; i++)
            {
                row[i] += (previous[i] + next[i]) >> 1;
            }
        }
    }

    private static void LiftIrreversibleColumns(Span<float> columns, int count, int height, int parity)
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
            var factor = ((y + parity) & 1) == 0 ? K : InverseK;
            foreach (ref var value in columns.Slice(y * ColumnStrip, count))
            {
                value *= factor;
            }
        }

        LiftStep(columns, count, height, even, Delta);
        LiftStep(columns, count, height, odd, Gamma);
        LiftStep(columns, count, height, even, Beta);
        LiftStep(columns, count, height, odd, Alpha);

        static void LiftStep(Span<float> columns, int count, int height, int start, float factor)
        {
            for (var y = start; y < height; y += 2)
            {
                var previous = columns.Slice(Mirror(y - 1, height) * ColumnStrip, count);
                var next = columns.Slice(Mirror(y + 1, height) * ColumnStrip, count);
                var row = columns.Slice(y * ColumnStrip, count);
                for (var i = 0; i < row.Length; i++)
                {
                    var sum = previous[i] + next[i];
                    var product = factor * sum;
                    row[i] -= product;
                }
            }
        }
    }
}
