using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

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

    // The rows of a strip the vector paths gather ahead of the lifting steps: a short loop of copies keeps several rows
    // of the plane in flight, and the rows are still in the cache when the steps read them
    private const int GatheredRows = 16;

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

    /// <summary>The scalar reference of <see cref="InverseReversible(Span{int}, int, ReadOnlySpan{Jpeg2000ResolutionGeometry}, Span{int})"/>.</summary>
    internal static void InverseReversibleScalar(Span<int> plane, int stride, ReadOnlySpan<Jpeg2000ResolutionGeometry> resolutions, Span<int> scratch)
        => InverseReversibleCore(plane, stride, resolutions, scratch, useVectors: false);

    private static void InverseReversibleCore(Span<int> plane, int stride, ReadOnlySpan<Jpeg2000ResolutionGeometry> resolutions, Span<int> scratch, bool useVectors)
    {
        for (var r = 1; r < resolutions.Length; r++)
        {
            ref readonly var lower = ref resolutions[r - 1];
            ref readonly var current = ref resolutions[r];
            if (current.Width == 0 || current.Height == 0)
                continue;

            InverseReversibleRows(plane, stride, in lower, in current, scratch, 0, current.Height, useVectors);
            InverseReversibleColumns(plane, stride, in lower, in current, scratch, 0, GetStripCount(current.Width), useVectors);
        }
    }

    /// <summary>
    /// Rebuilds the samples of a tile-component from its sub-bands, with the reversible filter, by several workers: the
    /// rows of a level are split into bands, then its strips of columns. A row, and a strip, is filtered as by one worker,
    /// in a scratch of its worker, and never reads the samples of another one.
    /// </summary>
    /// <param name="plane">The plane of the tile-component.</param>
    /// <param name="stride">The width of the plane.</param>
    /// <param name="resolutions">The resolution levels of the tile-component, from the lowest.</param>
    /// <param name="scratch">The scratch of each worker: at least <see cref="GetScratchLength"/> elements each.</param>
    /// <param name="workers">The largest number of workers: at most the length of <paramref name="scratch"/>.</param>
    public static void InverseReversible(Av1PooledArray<int> plane, int stride, ArraySegment<Jpeg2000ResolutionGeometry> resolutions, Av1PooledArray<int>[] scratch, int workers)
    {
        var useVectors = Vector.IsHardwareAccelerated;
        for (var r = 1; r < resolutions.Count; r++)
        {
            var lower = resolutions[r - 1];
            var current = resolutions[r];
            if (current.Width == 0 || current.Height == 0)
                continue;

            RunRows(workers, in current, (band, start, end) => InverseReversibleRows(plane.Span, stride, in lower, in current, scratch[band].Span, start, end, useVectors));
            RunStrips(workers, in current, (band, start, end) => InverseReversibleColumns(plane.Span, stride, in lower, in current, scratch[band].Span, start, end, useVectors));
        }
    }

    // HOR_SR on the rows [first, end) of a level
    private static void InverseReversibleRows(Span<int> plane, int stride, in Jpeg2000ResolutionGeometry lower, in Jpeg2000ResolutionGeometry current, Span<int> scratch, int first, int end, bool useVectors)
    {
        var width = current.Width;
        var lowWidth = lower.Width;
        var parityX = (int)(current.X0 & 1);
        var line = scratch[..width];
        for (var y = first; y < end; y++)
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
    }

    // VER_SR on the strips of columns [firstStrip, endStrip) of a level
    private static void InverseReversibleColumns(Span<int> plane, int stride, in Jpeg2000ResolutionGeometry lower, in Jpeg2000ResolutionGeometry current, Span<int> scratch, int firstStrip, int endStrip, bool useVectors)
    {
        var width = current.Width;
        var height = current.Height;
        var lowHeight = lower.Height;
        var parityY = (int)(current.Y0 & 1);
        var strip = Math.Min(ColumnStrip, width);
        var columns = scratch.Slice(width, strip * height);
        var endX = (int)Math.Min(width, (long)endStrip * strip);
        for (var x = firstStrip * strip; x < endX; x += strip)
        {
            var count = Math.Min(strip, width - x);
            if (useVectors && height > 1)
            {
                LiftReversibleColumnsPipelined(plane, stride, x, count, height, lowHeight, parityY, columns, strip);
            }
            else
            {
                GatherColumns(plane, stride, x, count, height, lowHeight, parityY, columns, strip);
                LiftReversibleColumns(columns, count, height, parityY, strip, useVectors);
            }

            ScatterColumns(plane, stride, x, count, height, columns, strip);
        }
    }

    /// <summary>Rebuilds the samples of a tile-component from its sub-bands, with the irreversible filter.</summary>
    /// <param name="plane">The plane of the tile-component.</param>
    /// <param name="stride">The width of the plane.</param>
    /// <param name="resolutions">The resolution levels of the tile-component, from the lowest.</param>
    /// <param name="scratch">At least <see cref="GetScratchLength"/> elements.</param>
    public static void InverseIrreversible(Span<float> plane, int stride, ReadOnlySpan<Jpeg2000ResolutionGeometry> resolutions, Span<float> scratch)
        => InverseIrreversibleCore(plane, stride, resolutions, scratch, Vector.IsHardwareAccelerated);

    /// <summary>The scalar reference of <see cref="InverseIrreversible(Span{float}, int, ReadOnlySpan{Jpeg2000ResolutionGeometry}, Span{float})"/>.</summary>
    internal static void InverseIrreversibleScalar(Span<float> plane, int stride, ReadOnlySpan<Jpeg2000ResolutionGeometry> resolutions, Span<float> scratch)
        => InverseIrreversibleCore(plane, stride, resolutions, scratch, useVectors: false);

    private static void InverseIrreversibleCore(Span<float> plane, int stride, ReadOnlySpan<Jpeg2000ResolutionGeometry> resolutions, Span<float> scratch, bool useVectors)
    {
        for (var r = 1; r < resolutions.Length; r++)
        {
            ref readonly var lower = ref resolutions[r - 1];
            ref readonly var current = ref resolutions[r];
            if (current.Width == 0 || current.Height == 0)
                continue;

            InverseIrreversibleRows(plane, stride, in lower, in current, scratch, 0, current.Height, useVectors);
            InverseIrreversibleColumns(plane, stride, in lower, in current, scratch, 0, GetStripCount(current.Width), useVectors);
        }
    }

    /// <summary>
    /// Rebuilds the samples of a tile-component from its sub-bands, with the irreversible filter, by several workers, as
    /// <see cref="InverseReversible(Av1PooledArray{int}, int, ArraySegment{Jpeg2000ResolutionGeometry}, Av1PooledArray{int}[], int)"/>
    /// does: the plane and the scratches hold single-precision numbers.
    /// </summary>
    public static void InverseIrreversible(Av1PooledArray<int> plane, int stride, ArraySegment<Jpeg2000ResolutionGeometry> resolutions, Av1PooledArray<int>[] scratch, int workers)
    {
        var useVectors = Vector.IsHardwareAccelerated;
        for (var r = 1; r < resolutions.Count; r++)
        {
            var lower = resolutions[r - 1];
            var current = resolutions[r];
            if (current.Width == 0 || current.Height == 0)
                continue;

            RunRows(workers, in current, (band, start, end) => InverseIrreversibleRows(AsSingle(plane), stride, in lower, in current, AsSingle(scratch[band]), start, end, useVectors));
            RunStrips(workers, in current, (band, start, end) => InverseIrreversibleColumns(AsSingle(plane), stride, in lower, in current, AsSingle(scratch[band]), start, end, useVectors));
        }
    }

    private static void InverseIrreversibleRows(Span<float> plane, int stride, in Jpeg2000ResolutionGeometry lower, in Jpeg2000ResolutionGeometry current, Span<float> scratch, int first, int end, bool useVectors)
    {
        var width = current.Width;
        var lowWidth = lower.Width;
        var parityX = (int)(current.X0 & 1);
        var line = scratch[..width];
        for (var y = first; y < end; y++)
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
    }

    private static void InverseIrreversibleColumns(Span<float> plane, int stride, in Jpeg2000ResolutionGeometry lower, in Jpeg2000ResolutionGeometry current, Span<float> scratch, int firstStrip, int endStrip, bool useVectors)
    {
        var width = current.Width;
        var height = current.Height;
        var lowHeight = lower.Height;
        var parityY = (int)(current.Y0 & 1);
        var strip = Math.Min(ColumnStrip, width);
        var columns = scratch.Slice(width, strip * height);
        var endX = (int)Math.Min(width, (long)endStrip * strip);
        for (var x = firstStrip * strip; x < endX; x += strip)
        {
            var count = Math.Min(strip, width - x);
            if (useVectors && height > 1)
            {
                LiftIrreversibleColumnsPipelined(plane, stride, x, count, height, lowHeight, parityY, columns, strip);
            }
            else
            {
                GatherColumns(plane, stride, x, count, height, lowHeight, parityY, columns, strip);
                LiftIrreversibleColumns(columns, count, height, parityY, strip, useVectors);
            }

            ScatterColumns(plane, stride, x, count, height, columns, strip);
        }
    }

    /// <summary>Decomposes the samples of a tile-component into its sub-bands, with the reversible filter (F.4).</summary>
    /// <param name="plane">The plane of the tile-component.</param>
    /// <param name="stride">The width of the plane.</param>
    /// <param name="resolutions">The resolution levels of the tile-component, from the lowest.</param>
    /// <param name="scratch">At least <see cref="GetScratchLength"/> elements.</param>
    public static void ForwardReversible(Span<int> plane, int stride, ReadOnlySpan<Jpeg2000ResolutionGeometry> resolutions, Span<int> scratch)
        => ForwardReversibleCore(plane, stride, resolutions, scratch, Vector.IsHardwareAccelerated);

    /// <summary>The scalar reference of <see cref="ForwardReversible(Span{int}, int, ReadOnlySpan{Jpeg2000ResolutionGeometry}, Span{int})"/>.</summary>
    internal static void ForwardReversibleScalar(Span<int> plane, int stride, ReadOnlySpan<Jpeg2000ResolutionGeometry> resolutions, Span<int> scratch)
        => ForwardReversibleCore(plane, stride, resolutions, scratch, useVectors: false);

    private static void ForwardReversibleCore(Span<int> plane, int stride, ReadOnlySpan<Jpeg2000ResolutionGeometry> resolutions, Span<int> scratch, bool useVectors)
    {
        for (var r = resolutions.Length - 1; r >= 1; r--)
        {
            ref readonly var lower = ref resolutions[r - 1];
            ref readonly var current = ref resolutions[r];
            if (current.Width == 0 || current.Height == 0)
                continue;

            // 2D_SD (F.4.2): the columns (VER_SD), then the rows (HOR_SD)
            ForwardReversibleColumns(plane, stride, in lower, in current, scratch, 0, GetStripCount(current.Width), useVectors);
            ForwardReversibleRows(plane, stride, in lower, in current, scratch, 0, current.Height, useVectors);
        }
    }

    /// <summary>
    /// Decomposes the samples of a tile-component into its sub-bands, with the reversible filter, by several workers: the
    /// strips of columns of a level are split into bands, then its rows, each filtered as by one worker.
    /// </summary>
    /// <param name="plane">The plane of the tile-component.</param>
    /// <param name="stride">The width of the plane.</param>
    /// <param name="resolutions">The resolution levels of the tile-component, from the lowest.</param>
    /// <param name="scratch">The scratch of each worker: at least <see cref="GetScratchLength"/> elements each.</param>
    /// <param name="workers">The largest number of workers: at most the length of <paramref name="scratch"/>.</param>
    public static void ForwardReversible(Av1PooledArray<int> plane, int stride, ArraySegment<Jpeg2000ResolutionGeometry> resolutions, Av1PooledArray<int>[] scratch, int workers)
    {
        var useVectors = Vector.IsHardwareAccelerated;
        for (var r = resolutions.Count - 1; r >= 1; r--)
        {
            var lower = resolutions[r - 1];
            var current = resolutions[r];
            if (current.Width == 0 || current.Height == 0)
                continue;

            RunStrips(workers, in current, (band, start, end) => ForwardReversibleColumns(plane.Span, stride, in lower, in current, scratch[band].Span, start, end, useVectors));
            RunRows(workers, in current, (band, start, end) => ForwardReversibleRows(plane.Span, stride, in lower, in current, scratch[band].Span, start, end, useVectors));
        }
    }

    private static void ForwardReversibleColumns(Span<int> plane, int stride, in Jpeg2000ResolutionGeometry lower, in Jpeg2000ResolutionGeometry current, Span<int> scratch, int firstStrip, int endStrip, bool useVectors)
    {
        var width = current.Width;
        var height = current.Height;
        var lowHeight = lower.Height;
        var parityY = (int)(current.Y0 & 1);
        var strip = Math.Min(ColumnStrip, width);
        var columns = scratch.Slice(width, strip * height);
        var endX = (int)Math.Min(width, (long)endStrip * strip);
        for (var x = firstStrip * strip; x < endX; x += strip)
        {
            var count = Math.Min(strip, width - x);
            GatherRows(plane, stride, x, count, height, columns, strip);
            LiftForwardReversibleColumns(columns, count, height, parityY, strip, useVectors);
            ScatterColumnsDeinterleaved(plane, stride, x, count, height, lowHeight, parityY, columns, strip);
        }
    }

    private static void ForwardReversibleRows(Span<int> plane, int stride, in Jpeg2000ResolutionGeometry lower, in Jpeg2000ResolutionGeometry current, Span<int> scratch, int first, int end, bool useVectors)
    {
        var width = current.Width;
        var lowWidth = lower.Width;
        var parityX = (int)(current.X0 & 1);
        var line = scratch[..width];
        for (var y = first; y < end; y++)
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

    /// <summary>Decomposes the samples of a tile-component into its sub-bands, with the irreversible filter (F.4).</summary>
    /// <param name="plane">The plane of the tile-component.</param>
    /// <param name="stride">The width of the plane.</param>
    /// <param name="resolutions">The resolution levels of the tile-component, from the lowest.</param>
    /// <param name="scratch">At least <see cref="GetScratchLength"/> elements.</param>
    public static void ForwardIrreversible(Span<float> plane, int stride, ReadOnlySpan<Jpeg2000ResolutionGeometry> resolutions, Span<float> scratch)
        => ForwardIrreversibleCore(plane, stride, resolutions, scratch, Vector.IsHardwareAccelerated);

    /// <summary>The scalar reference of <see cref="ForwardIrreversible(Span{float}, int, ReadOnlySpan{Jpeg2000ResolutionGeometry}, Span{float})"/>.</summary>
    internal static void ForwardIrreversibleScalar(Span<float> plane, int stride, ReadOnlySpan<Jpeg2000ResolutionGeometry> resolutions, Span<float> scratch)
        => ForwardIrreversibleCore(plane, stride, resolutions, scratch, useVectors: false);

    private static void ForwardIrreversibleCore(Span<float> plane, int stride, ReadOnlySpan<Jpeg2000ResolutionGeometry> resolutions, Span<float> scratch, bool useVectors)
    {
        for (var r = resolutions.Length - 1; r >= 1; r--)
        {
            ref readonly var lower = ref resolutions[r - 1];
            ref readonly var current = ref resolutions[r];
            if (current.Width == 0 || current.Height == 0)
                continue;

            ForwardIrreversibleColumns(plane, stride, in lower, in current, scratch, 0, GetStripCount(current.Width), useVectors);
            ForwardIrreversibleRows(plane, stride, in lower, in current, scratch, 0, current.Height, useVectors);
        }
    }

    /// <summary>
    /// Decomposes the samples of a tile-component into its sub-bands, with the irreversible filter, by several workers, as
    /// <see cref="ForwardReversible(Av1PooledArray{int}, int, ArraySegment{Jpeg2000ResolutionGeometry}, Av1PooledArray{int}[], int)"/>
    /// does: the plane and the scratches hold single-precision numbers.
    /// </summary>
    public static void ForwardIrreversible(Av1PooledArray<int> plane, int stride, ArraySegment<Jpeg2000ResolutionGeometry> resolutions, Av1PooledArray<int>[] scratch, int workers)
    {
        var useVectors = Vector.IsHardwareAccelerated;
        for (var r = resolutions.Count - 1; r >= 1; r--)
        {
            var lower = resolutions[r - 1];
            var current = resolutions[r];
            if (current.Width == 0 || current.Height == 0)
                continue;

            RunStrips(workers, in current, (band, start, end) => ForwardIrreversibleColumns(AsSingle(plane), stride, in lower, in current, AsSingle(scratch[band]), start, end, useVectors));
            RunRows(workers, in current, (band, start, end) => ForwardIrreversibleRows(AsSingle(plane), stride, in lower, in current, AsSingle(scratch[band]), start, end, useVectors));
        }
    }

    private static void ForwardIrreversibleColumns(Span<float> plane, int stride, in Jpeg2000ResolutionGeometry lower, in Jpeg2000ResolutionGeometry current, Span<float> scratch, int firstStrip, int endStrip, bool useVectors)
    {
        var width = current.Width;
        var height = current.Height;
        var lowHeight = lower.Height;
        var parityY = (int)(current.Y0 & 1);
        var strip = Math.Min(ColumnStrip, width);
        var columns = scratch.Slice(width, strip * height);
        var endX = (int)Math.Min(width, (long)endStrip * strip);
        for (var x = firstStrip * strip; x < endX; x += strip)
        {
            var count = Math.Min(strip, width - x);
            GatherRows(plane, stride, x, count, height, columns, strip);
            LiftForwardIrreversibleColumns(columns, count, height, parityY, strip, useVectors);
            ScatterColumnsDeinterleaved(plane, stride, x, count, height, lowHeight, parityY, columns, strip);
        }
    }

    private static void ForwardIrreversibleRows(Span<float> plane, int stride, in Jpeg2000ResolutionGeometry lower, in Jpeg2000ResolutionGeometry current, Span<float> scratch, int first, int end, bool useVectors)
    {
        var width = current.Width;
        var lowWidth = lower.Width;
        var parityX = (int)(current.X0 & 1);
        var line = scratch[..width];
        for (var y = first; y < end; y++)
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

    // The number of strips of ColumnStrip columns of a level
    private static int GetStripCount(int width) => (width + ColumnStrip - 1) / ColumnStrip;

    private static Span<float> AsSingle(Av1PooledArray<int> array) => unsafe(MemoryMarshal.Cast<int, float>(array.Span));

    // Runs a pass on the rows of a level: in bands of at least RowBands.MinRowsPerBand rows, one per worker
    private static void RunRows(int workers, in Jpeg2000ResolutionGeometry level, Action<int, int, int> rows)
    {
        var height = level.Height;
        var bands = Jpeg2000Workers.GetBandCount(workers, (long)level.Width * height, height, RowBands.MinRowsPerBand);
        Jpeg2000Workers.RunBands(bands, band => rows(band, Jpeg2000Workers.GetBandStart(height, band, bands), Jpeg2000Workers.GetBandStart(height, band + 1, bands)));
    }

    // Runs a pass on the strips of columns of a level: contiguous strips, one band per worker
    private static void RunStrips(int workers, in Jpeg2000ResolutionGeometry level, Action<int, int, int> strips)
    {
        var count = GetStripCount(level.Width);
        var bands = Jpeg2000Workers.GetBandCount(workers, (long)level.Width * level.Height, count, 1);
        Jpeg2000Workers.RunBands(bands, band => strips(band, Jpeg2000Workers.GetBandStart(count, band, bands), Jpeg2000Workers.GetBandStart(count, band + 1, bands)));
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
        if (useVectors && Unsafe.SizeOf<T>() == sizeof(uint) && (AdvSimd.Arm64.IsSupported || Sse2.IsSupported))
        {
            // The samples of "first" and "second" alternate (zip, unpack). The row starts with a sample of "first", which
            // has as many samples as "second" or one more
            var first = parity == 0 ? low : high;
            var second = parity == 0 ? high : low;
            line = line[..row.Length];
            ref var firstStart = ref unsafe(Unsafe.As<T, uint>(ref MemoryMarshal.GetReference(first)));
            ref var secondStart = ref unsafe(Unsafe.As<T, uint>(ref MemoryMarshal.GetReference(second)));
            ref var lineStart = ref unsafe(Unsafe.As<T, uint>(ref MemoryMarshal.GetReference(line)));
            for (; i <= second.Length - Vector128<uint>.Count; i += Vector128<uint>.Count)
            {
                var firstValues = unsafe(Vector128.LoadUnsafe(ref firstStart, (nuint)i));
                var secondValues = unsafe(Vector128.LoadUnsafe(ref secondStart, (nuint)i));
                Vector128<uint> start;
                Vector128<uint> end;
                if (AdvSimd.Arm64.IsSupported)
                {
                    start = AdvSimd.Arm64.ZipLow(firstValues, secondValues);
                    end = AdvSimd.Arm64.ZipHigh(firstValues, secondValues);
                }
                else
                {
                    start = Sse2.UnpackLow(firstValues, secondValues);
                    end = Sse2.UnpackHigh(firstValues, secondValues);
                }

                unsafe { start.StoreUnsafe(ref lineStart, (nuint)(2 * i)); }
                unsafe { end.StoreUnsafe(ref lineStart, (nuint)((2 * i) + Vector128<uint>.Count)); }
            }
        }
        else if (useVectors && BitConverter.IsLittleEndian && Vector<T>.Count == Vector<uint>.Count)
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
        => GatherColumnRows(plane, stride, x, count, 0, height, lowCount, parity, columns, strip);

    /// <summary>Copies the rows <paramref name="start"/> to <paramref name="end"/> (excluded) of a strip of columns, with the rows interleaved.</summary>
    private static void GatherColumnRows<T>(ReadOnlySpan<T> plane, int stride, int x, int count, int start, int end, int lowCount, int parity, Span<T> columns, int strip)
    {
        for (var y = start; y < end; y++)
        {
            var isLow = ((y + parity) & 1) == 0;
            var source = isLow ? y >> 1 : lowCount + (y >> 1);
            plane.Slice((source * stride) + x, count).CopyTo(columns.Slice(y * strip, count));
        }
    }

    /// <summary>
    /// Copies the rows <paramref name="start"/> to <paramref name="end"/> (excluded) of a strip of columns, with the rows
    /// interleaved and multiplied by the scaling factor of their parity (F.3.8.2).
    /// </summary>
    private static void GatherScaledColumnRows(ReadOnlySpan<float> plane, int stride, int x, int count, int start, int end, int lowCount, int parity, Span<float> columns, int strip)
    {
        for (var y = start; y < end; y++)
        {
            var isLow = ((y + parity) & 1) == 0;
            var source = isLow ? y >> 1 : lowCount + (y >> 1);
            Scale(columns.Slice(y * strip, count), plane.Slice((source * stride) + x, count), isLow ? K : InverseK, useVectors: true);
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
        if (useVectors && Unsafe.SizeOf<T>() == sizeof(uint) && (AdvSimd.Arm64.IsSupported || Sse.IsSupported))
        {
            // The reverse of Interleave: the samples at even positions of the line go to "first" (unzip, shuffle)
            var first = parity == 0 ? low : high;
            var second = parity == 0 ? high : low;
            line = line[..row.Length];
            ref var firstStart = ref unsafe(Unsafe.As<T, uint>(ref MemoryMarshal.GetReference(first)));
            ref var secondStart = ref unsafe(Unsafe.As<T, uint>(ref MemoryMarshal.GetReference(second)));
            ref var lineStart = ref unsafe(Unsafe.As<T, uint>(ref MemoryMarshal.GetReference(line)));
            for (; i <= second.Length - Vector128<uint>.Count; i += Vector128<uint>.Count)
            {
                var start = unsafe(Vector128.LoadUnsafe(ref lineStart, (nuint)(2 * i)));
                var end = unsafe(Vector128.LoadUnsafe(ref lineStart, (nuint)((2 * i) + Vector128<uint>.Count)));
                Vector128<uint> firstValues;
                Vector128<uint> secondValues;
                if (AdvSimd.Arm64.IsSupported)
                {
                    firstValues = AdvSimd.Arm64.UnzipEven(start, end);
                    secondValues = AdvSimd.Arm64.UnzipOdd(start, end);
                }
                else
                {
                    firstValues = Sse.Shuffle(start.AsSingle(), end.AsSingle(), 0b10_00_10_00).AsUInt32();
                    secondValues = Sse.Shuffle(start.AsSingle(), end.AsSingle(), 0b11_01_11_01).AsUInt32();
                }

                unsafe { firstValues.StoreUnsafe(ref firstStart, (nuint)i); }
                unsafe { secondValues.StoreUnsafe(ref secondStart, (nuint)i); }
            }
        }
        else if (useVectors && BitConverter.IsLittleEndian && Vector<T>.Count == Vector<uint>.Count)
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

    /// <summary>
    /// The vector path of <see cref="GatherColumns"/> then <see cref="LiftReversibleColumns"/>, in one pass down the strip:
    /// the row of odd positions above a row of even positions is lifted (F-6) right after that row (F-5), and the rows are
    /// gathered a few at a time just before, so that the rows a step reads are still in the cache. The two steps of the
    /// rows away from the ends are applied in one loop.
    /// </summary>
    private static void LiftReversibleColumnsPipelined(ReadOnlySpan<int> plane, int stride, int x, int count, int height, int lowCount, int parity, Span<int> columns, int strip)
    {
        var gathered = 0;
        for (var y = 0; y <= height; y++)
        {
            if (((y + parity) & 1) != 0)
                continue;

            // F-5 on the row y reads the row y + 1
            if (gathered < height && gathered <= y + 1)
            {
                var end = Math.Min(height, y + 2 + GatheredRows);
                GatherColumnRows(plane, stride, x, count, gathered, end, lowCount, parity, columns, strip);
                gathered = end;
            }

            // F-5 on the row y, then F-6 on the row y - 1, which has the rows y - 2 and y around it
            if (y >= 2 && y <= height - 2)
            {
                LiftReversibleRows(columns.Slice((y - 2) * strip, (3 * strip) + count), count, strip);
                continue;
            }

            if (y < height)
            {
                LiftColumnRow(columns, count, height, strip, y, bias: 2, shift: 2, subtract: true);
            }

            if (y >= 1)
            {
                LiftColumnRow(columns, count, height, strip, y - 1, bias: 0, shift: 1, subtract: false);
            }
        }

        static void LiftColumnRow(Span<int> columns, int count, int height, int strip, int y, int bias, int shift, bool subtract)
        {
            var previous = columns.Slice(Mirror(y - 1, height) * strip, count);
            var next = columns.Slice(Mirror(y + 1, height) * strip, count);
            Lift(columns.Slice(y * strip, count), previous, next, bias, shift, subtract, useVectors: true);
        }
    }

    /// <summary>
    /// F-5 on the third of four rows of a strip, then F-6 on the second one, in one loop: <paramref name="rows"/> starts with
    /// the first row and holds the four of them.
    /// </summary>
    private static void LiftReversibleRows(Span<int> rows, int count, int strip)
    {
        var i = 0;
        ref var row0 = ref unsafe(MemoryMarshal.GetReference(rows));
        ref var row1 = ref unsafe(Unsafe.Add(ref row0, strip));
        ref var row2 = ref unsafe(Unsafe.Add(ref row1, strip));
        ref var row3 = ref unsafe(Unsafe.Add(ref row2, strip));
        var two = new Vector<int>(2);
        for (; i <= count - Vector<int>.Count; i += Vector<int>.Count)
        {
            var index = (nuint)i;
            var above = unsafe(Vector.LoadUnsafe(ref row0, index));
            var odd = unsafe(Vector.LoadUnsafe(ref row1, index));
            var even = unsafe(Vector.LoadUnsafe(ref row2, index));
            var below = unsafe(Vector.LoadUnsafe(ref row3, index));
            even -= (odd + below + two) >> 2;
            odd += (above + even) >> 1;
            unsafe { even.StoreUnsafe(ref row2, index); }
            unsafe { odd.StoreUnsafe(ref row1, index); }
        }

        for (; i < count; i++)
        {
            var even = rows[(2 * strip) + i] - ((rows[strip + i] + rows[(3 * strip) + i] + 2) >> 2);
            rows[(2 * strip) + i] = even;
            rows[strip + i] += (rows[i] + even) >> 1;
        }
    }

    /// <summary>
    /// The vector path of <see cref="GatherColumns"/> then <see cref="LiftIrreversibleColumns"/>, in one pass down the
    /// strip: the rows are gathered and scaled a few at a time, and each lifting step runs one row behind the step before
    /// it, so that the rows it reads have been through that step and not through the next one. The four steps of the rows
    /// away from the ends are applied in one loop.
    /// </summary>
    private static void LiftIrreversibleColumnsPipelined(ReadOnlySpan<float> plane, int stride, int x, int count, int height, int lowCount, int parity, Span<float> columns, int strip)
    {
        var gathered = 0;
        for (var y = 0; y < height + 3; y++)
        {
            if (((y + parity) & 1) != 0)
                continue;

            // The step delta on the row y reads the row y + 1
            if (gathered < height && gathered <= y + 1)
            {
                var end = Math.Min(height, y + 2 + GatheredRows);
                GatherScaledColumnRows(plane, stride, x, count, gathered, end, lowCount, parity, columns, strip);
                gathered = end;
            }

            if (y >= 4 && y <= height - 2)
            {
                LiftIrreversibleRows(columns.Slice((y - 4) * strip, (5 * strip) + count), count, strip);
                continue;
            }

            LiftColumnRow(columns, count, height, strip, y, Delta);
            LiftColumnRow(columns, count, height, strip, y - 1, Gamma);
            LiftColumnRow(columns, count, height, strip, y - 2, Beta);
            LiftColumnRow(columns, count, height, strip, y - 3, Alpha);
        }

        static void LiftColumnRow(Span<float> columns, int count, int height, int strip, int y, float factor)
        {
            if (y < 0 || y >= height)
                return;

            var previous = columns.Slice(Mirror(y - 1, height) * strip, count);
            var next = columns.Slice(Mirror(y + 1, height) * strip, count);
            Lift(columns.Slice(y * strip, count), previous, next, factor, subtract: true, useVectors: true);
        }
    }

    /// <summary>
    /// The four lifting steps of the inverse irreversible filter on six rows of a strip, in one loop: the step delta on the
    /// fifth row, gamma on the fourth, beta on the third and alpha on the second, each one reading the row the step before
    /// it has just changed. <paramref name="rows"/> starts with the first row and holds the six of them.
    /// </summary>
    private static void LiftIrreversibleRows(Span<float> rows, int count, int strip)
    {
        // The sum, the product and the difference are three operations, like the scalar ones: nothing is fused
        var i = 0;
        ref var row0 = ref unsafe(MemoryMarshal.GetReference(rows));
        ref var row1 = ref unsafe(Unsafe.Add(ref row0, strip));
        ref var row2 = ref unsafe(Unsafe.Add(ref row1, strip));
        ref var row3 = ref unsafe(Unsafe.Add(ref row2, strip));
        ref var row4 = ref unsafe(Unsafe.Add(ref row3, strip));
        ref var row5 = ref unsafe(Unsafe.Add(ref row4, strip));
        var alpha = new Vector<float>(Alpha);
        var beta = new Vector<float>(Beta);
        var gamma = new Vector<float>(Gamma);
        var delta = new Vector<float>(Delta);
        for (; i <= count - Vector<float>.Count; i += Vector<float>.Count)
        {
            var index = (nuint)i;
            var value0 = unsafe(Vector.LoadUnsafe(ref row0, index));
            var value1 = unsafe(Vector.LoadUnsafe(ref row1, index));
            var value2 = unsafe(Vector.LoadUnsafe(ref row2, index));
            var value3 = unsafe(Vector.LoadUnsafe(ref row3, index));
            var value4 = unsafe(Vector.LoadUnsafe(ref row4, index));
            var value5 = unsafe(Vector.LoadUnsafe(ref row5, index));
            value4 -= delta * (value3 + value5);
            value3 -= gamma * (value2 + value4);
            value2 -= beta * (value1 + value3);
            value1 -= alpha * (value0 + value2);
            unsafe { value4.StoreUnsafe(ref row4, index); }
            unsafe { value3.StoreUnsafe(ref row3, index); }
            unsafe { value2.StoreUnsafe(ref row2, index); }
            unsafe { value1.StoreUnsafe(ref row1, index); }
        }

        for (; i < count; i++)
        {
            var value4 = rows[(4 * strip) + i] - (Delta * (rows[(3 * strip) + i] + rows[(5 * strip) + i]));
            var value3 = rows[(3 * strip) + i] - (Gamma * (rows[(2 * strip) + i] + value4));
            var value2 = rows[(2 * strip) + i] - (Beta * (rows[strip + i] + value3));
            rows[(4 * strip) + i] = value4;
            rows[(3 * strip) + i] = value3;
            rows[(2 * strip) + i] = value2;
            rows[strip + i] -= Alpha * (rows[i] + value2);
        }
    }

    /// <summary>The reversible filter on a row whose halves are not interleaved yet: what <see cref="LiftReversible"/> does to the interleaved row.</summary>
    private static void LiftReversibleSeparated(Span<int> low, Span<int> high, int parity)
    {
        LiftHalf<Set>(low, high, parity, bias: 2, shift: 2);
        LiftHalf<NotSet>(high, low, 1 - parity, bias: 0, shift: 1);
    }

    /// <summary>The forward reversible filter on a row whose halves are separated already: what <see cref="LiftForwardReversible"/> does to the row.</summary>
    private static void LiftForwardReversibleSeparated(Span<int> low, Span<int> high, int parity)
    {
        LiftHalf<Set>(high, low, 1 - parity, bias: 0, shift: 1);
        LiftHalf<NotSet>(low, high, parity, bias: 2, shift: 2);
    }

    /// <summary>
    /// The irreversible filter on a row whose halves are not interleaved yet: what <see cref="LiftIrreversible"/> does to the
    /// interleaved row. The first two lifting steps apply the scaling: delta multiplies the low-pass samples it changes by
    /// K and the high-pass samples it reads by 1/K, then gamma multiplies the high-pass samples it changes by 1/K, so that
    /// a step reads each sample multiplied by its factor, as it does after the scaling step.
    /// </summary>
    private static void LiftIrreversibleSeparated(Span<float> low, Span<float> high, int parity)
    {
        LiftHalf<Set, Set, Set, NotSet>(low, high, parity, Delta, K, InverseK, resultScale: 1);
        LiftHalf<Set, Set, NotSet, NotSet>(high, low, 1 - parity, Gamma, InverseK, sourceScale: 1, resultScale: 1);
        LiftHalf<Set, NotSet, NotSet, NotSet>(low, high, parity, Beta, targetScale: 1, sourceScale: 1, resultScale: 1);
        LiftHalf<Set, NotSet, NotSet, NotSet>(high, low, 1 - parity, Alpha, targetScale: 1, sourceScale: 1, resultScale: 1);
    }

    /// <summary>
    /// The forward irreversible filter on a row whose halves are separated already: what <see cref="LiftForwardIrreversible"/>
    /// does to the row. The last lifting step multiplies the low-pass samples by 1/K as it writes them.
    /// </summary>
    private static void LiftForwardIrreversibleSeparated(Span<float> low, Span<float> high, int parity)
    {
        LiftHalf<NotSet, NotSet, NotSet, NotSet>(high, low, 1 - parity, Alpha, targetScale: 1, sourceScale: 1, resultScale: 1);
        LiftHalf<NotSet, NotSet, NotSet, NotSet>(low, high, parity, Beta, targetScale: 1, sourceScale: 1, resultScale: 1);
        LiftHalf<NotSet, NotSet, NotSet, NotSet>(high, low, 1 - parity, Gamma, targetScale: 1, sourceScale: 1, resultScale: 1);
        LiftHalf<NotSet, NotSet, NotSet, Set>(low, high, parity, Delta, targetScale: 1, sourceScale: 1, InverseK);
        Scale(high, K, useVectors: true);
    }

    /// <summary>
    /// A lifting step of the reversible filter on one half of a row of at least two samples. The sample
    /// <c>target[i]</c> lies between <c>source[i + offset - 1]</c> and <c>source[i + offset]</c>; at an end of the row, the
    /// neighbor that does not exist is the other one (the symmetric extension of F.3.7).
    /// </summary>
    /// <typeparam name="TSubtract">Whether the term is subtracted from the samples rather than added to them.</typeparam>
    /// <param name="target">The half that changes.</param>
    /// <param name="source">The other half.</param>
    /// <param name="offset">1 when the row starts with a sample of <paramref name="source"/>; otherwise, 0.</param>
    /// <param name="bias">The rounding term of the step.</param>
    /// <param name="shift">The shift of the step.</param>
    private static void LiftHalf<TSubtract>(Span<int> target, ReadOnlySpan<int> source, int offset, int bias, int shift)
        where TSubtract : struct, IFlag
    {
        var start = 1 - offset;
        var end = Math.Min(target.Length, source.Length - offset);
        if (start > 0)
        {
            Lift<TSubtract>(target[..1], source[..1], source[..1], bias, shift, useVectors: false);
        }

        Lift<TSubtract>(target[start..end], source[..(end - start)], source.Slice(1, end - start), bias, shift, useVectors: true);
        if (end < target.Length)
        {
            var last = source.Slice(end + offset - 1, 1);
            Lift<TSubtract>(target.Slice(end, 1), last, last, bias, shift, useVectors: false);
        }
    }

    /// <summary>A lifting step of the irreversible filter on one half of a row of at least two samples, with the scaling factors that are applied with it.</summary>
    /// <typeparam name="TSubtract">Whether the term is subtracted from the samples rather than added to them.</typeparam>
    /// <typeparam name="TScaleTarget">Whether the samples that change are multiplied by <paramref name="targetScale"/> first.</typeparam>
    /// <typeparam name="TScaleSource">Whether the samples of the other half are multiplied by <paramref name="sourceScale"/> as they are read.</typeparam>
    /// <typeparam name="TScaleResult">Whether the results are multiplied by <paramref name="resultScale"/>.</typeparam>
    /// <param name="target">The half that changes.</param>
    /// <param name="source">The other half.</param>
    /// <param name="offset">1 when the row starts with a sample of <paramref name="source"/>; otherwise, 0.</param>
    /// <param name="factor">The lifting parameter of the step.</param>
    /// <param name="targetScale">The factor of the samples that change.</param>
    /// <param name="sourceScale">The factor of the samples that are read.</param>
    /// <param name="resultScale">The factor of the results.</param>
    private static void LiftHalf<TSubtract, TScaleTarget, TScaleSource, TScaleResult>(Span<float> target, ReadOnlySpan<float> source, int offset, float factor, float targetScale, float sourceScale, float resultScale)
        where TSubtract : struct, IFlag
        where TScaleTarget : struct, IFlag
        where TScaleSource : struct, IFlag
        where TScaleResult : struct, IFlag
    {
        var start = 1 - offset;
        var end = Math.Min(target.Length, source.Length - offset);
        if (start > 0)
        {
            Lift<TSubtract, TScaleTarget, TScaleSource, TScaleResult>(target[..1], source[..1], source[..1], factor, targetScale, sourceScale, resultScale, useVectors: false);
        }

        Lift<TSubtract, TScaleTarget, TScaleSource, TScaleResult>(target[start..end], source[..(end - start)], source.Slice(1, end - start), factor, targetScale, sourceScale, resultScale, useVectors: true);
        if (end < target.Length)
        {
            var last = source.Slice(end + offset - 1, 1);
            Lift<TSubtract, TScaleTarget, TScaleSource, TScaleResult>(target.Slice(end, 1), last, last, factor, targetScale, sourceScale, resultScale, useVectors: false);
        }
    }

    /// <summary>A lifting step of the reversible filter: <c>target[i] -= (first[i] + second[i] + bias) >> shift</c>, or the addition.</summary>
    private static void Lift(Span<int> target, ReadOnlySpan<int> first, ReadOnlySpan<int> second, int bias, int shift, bool subtract, bool useVectors)
    {
        if (subtract)
        {
            Lift<Set>(target, first, second, bias, shift, useVectors);
        }
        else
        {
            Lift<NotSet>(target, first, second, bias, shift, useVectors);
        }
    }

    /// <summary>A lifting step of the reversible filter: <c>target[i] -= (first[i] + second[i] + bias) >> shift</c>, or the addition.</summary>
    private static void Lift<TSubtract>(Span<int> target, ReadOnlySpan<int> first, ReadOnlySpan<int> second, int bias, int shift, bool useVectors)
        where TSubtract : struct, IFlag
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
                unsafe { (TSubtract.IsSet ? value - term : value + term).StoreUnsafe(ref targetStart, (nuint)i); }
            }
        }

        for (; i < target.Length; i++)
        {
            var term = (first[i] + second[i] + bias) >> shift;
            target[i] = TSubtract.IsSet ? target[i] - term : target[i] + term;
        }
    }

    /// <summary>A lifting step of the irreversible filter: <c>target[i] -= factor * (first[i] + second[i])</c>, or the addition.</summary>
    private static void Lift(Span<float> target, ReadOnlySpan<float> first, ReadOnlySpan<float> second, float factor, bool subtract, bool useVectors)
    {
        if (subtract)
        {
            Lift<Set, NotSet, NotSet, NotSet>(target, first, second, factor, targetScale: 1, sourceScale: 1, resultScale: 1, useVectors);
        }
        else
        {
            Lift<NotSet, NotSet, NotSet, NotSet>(target, first, second, factor, targetScale: 1, sourceScale: 1, resultScale: 1, useVectors);
        }
    }

    /// <summary>
    /// A lifting step of the irreversible filter: <c>target[i] -= factor * (first[i] + second[i])</c>, or the addition,
    /// with the samples that change, the samples that are read and the results multiplied by their scaling factors when
    /// the corresponding type argument is <see cref="Set"/>. Each multiplication is the one the scaling step would make.
    /// </summary>
    private static void Lift<TSubtract, TScaleTarget, TScaleSource, TScaleResult>(Span<float> target, ReadOnlySpan<float> first, ReadOnlySpan<float> second, float factor, float targetScale, float sourceScale, float resultScale, bool useVectors)
        where TSubtract : struct, IFlag
        where TScaleTarget : struct, IFlag
        where TScaleSource : struct, IFlag
        where TScaleResult : struct, IFlag
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
            var targetScales = new Vector<float>(targetScale);
            var sourceScales = new Vector<float>(sourceScale);
            var resultScales = new Vector<float>(resultScale);
            for (; i <= target.Length - Vector<float>.Count; i += Vector<float>.Count)
            {
                var previous = unsafe(Vector.LoadUnsafe(ref firstStart, (nuint)i));
                var next = unsafe(Vector.LoadUnsafe(ref secondStart, (nuint)i));
                if (TScaleSource.IsSet)
                {
                    previous *= sourceScales;
                    next *= sourceScales;
                }

                var product = factors * (previous + next);
                var value = unsafe(Vector.LoadUnsafe(ref targetStart, (nuint)i));
                if (TScaleTarget.IsSet)
                {
                    value *= targetScales;
                }

                var result = TSubtract.IsSet ? value - product : value + product;
                if (TScaleResult.IsSet)
                {
                    result *= resultScales;
                }

                unsafe { result.StoreUnsafe(ref targetStart, (nuint)i); }
            }
        }

        for (; i < target.Length; i++)
        {
            var previous = first[i];
            var next = second[i];
            if (TScaleSource.IsSet)
            {
                previous *= sourceScale;
                next *= sourceScale;
            }

            var sum = previous + next;
            var product = factor * sum;
            var value = target[i];
            if (TScaleTarget.IsSet)
            {
                value *= targetScale;
            }

            var result = TSubtract.IsSet ? value - product : value + product;
            if (TScaleResult.IsSet)
            {
                result *= resultScale;
            }

            target[i] = result;
        }
    }

    /// <summary>The scaling step of the irreversible filter: <c>values[i] *= factor</c>.</summary>
    private static void Scale(Span<float> values, float factor, bool useVectors) => Scale(values, values, factor, useVectors);

    /// <summary>The scaling step of the irreversible filter: <c>destination[i] = source[i] * factor</c>.</summary>
    private static void Scale(Span<float> destination, ReadOnlySpan<float> source, float factor, bool useVectors)
    {
        // The vectors are read without a bound check: the source has the length of the destination
        source = source[..destination.Length];
        var i = 0;
        if (useVectors)
        {
            ref var destinationStart = ref unsafe(MemoryMarshal.GetReference(destination));
            ref var sourceStart = ref unsafe(MemoryMarshal.GetReference(source));
            var factors = new Vector<float>(factor);
            for (; i <= destination.Length - Vector<float>.Count; i += Vector<float>.Count)
            {
                unsafe { (Vector.LoadUnsafe(ref sourceStart, (nuint)i) * factors).StoreUnsafe(ref destinationStart, (nuint)i); }
            }
        }

        for (; i < destination.Length; i++)
        {
            destination[i] = source[i] * factor;
        }
    }

    /// <summary>A choice that is made when a generic method is compiled, so that its loops do not test it.</summary>
    private interface IFlag
    {
        static abstract bool IsSet { get; }
    }

    private readonly struct Set : IFlag
    {
        public static bool IsSet => true;
    }

    private readonly struct NotSet : IFlag
    {
        public static bool IsSet => false;
    }
}
