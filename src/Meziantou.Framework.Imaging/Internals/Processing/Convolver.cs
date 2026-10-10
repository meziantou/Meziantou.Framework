using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The in-place convolution of <see cref="ImageProcessingExtensions.Convolve(Image, ConvolutionOptions, CancellationToken)"/>
///.
/// </summary>
/// <remarks>
/// <para>
/// Rows are loaded into double-precision working samples (<see cref="WorkingSamples"/>), padded on both sides with the
/// pixels the kernel reads outside the image, so that every weight of a kernel row adds one whole shifted row to the
/// accumulator: <c>0 + w0 x0 + w1 x1 + ...</c>, kernel rows from top to bottom and weights from left to right, zero
/// weights skipped. The row additions are those of the resampler (<see cref="Resampler.AddScaled"/>): vectorized when
/// supported, without fused multiply-adds, so the result is bit-identical to the scalar loops.
/// </para>
/// <para>
/// The frame is rewritten in place, band by band. A band keeps the working rows it still needs in a ring before it
/// overwrites them, and reads the rows outside its range (those of the neighboring bands, and whatever the edge mode
/// designates beyond the image) from halos loaded for every band before any row is written. Every output sample is
/// therefore computed from original pixels by the same operations in the same order whatever the number of bands.
/// </para>
/// <para>Cancellation is observed between row bands; the rows already written stay written.</para>
/// </remarks>
internal static class Convolver
{
    private static readonly AsyncLocal<Action<int, bool>?> TestBandObserverValue = new();

    /// <summary>Gets or sets a test hook called when a row band starts (<see langword="true"/>) and ends, on the worker thread.</summary>
    internal static Action<int, bool>? TestBandObserver
    {
        get => TestBandObserverValue.Value;
        set => TestBandObserverValue.Value = value;
    }

    public static void Convolve<TPixel>(scoped in PixelLease lease, ConvolutionPlan plan, CancellationToken cancellationToken)
        where TPixel : unmanaged
    {
        // The lease is held by the caller for the whole operation; the workers use the leased storage directly (each
        // reads and writes the rows of its own band)
        var storage = lease.Storage;
        if (Unsafe.SizeOf<TPixel>() != storage.BytesPerPixel)
            throw new InvalidOperationException($"The pixel type '{typeof(TPixel).Name}' does not match the storage pixel size.");

        if (storage.Width != plan.Size.Width || storage.Height != plan.Size.Height)
            throw new InvalidOperationException("The storage does not have the size the convolution was prepared for.");

        if (typeof(TPixel) == typeof(Rgba64) || typeof(TPixel) == typeof(Gray16))
        {
            Convolve<ushort>(storage, plan, cancellationToken);
        }
        else if (typeof(TPixel) == typeof(Rgba32) || typeof(TPixel) == typeof(Bgra32) || typeof(TPixel) == typeof(Rgb24) || typeof(TPixel) == typeof(Gray8))
        {
            Convolve<byte>(storage, plan, cancellationToken);
        }
        else
        {
            throw new NotSupportedException($"The pixel type '{typeof(TPixel).Name}' is not supported.");
        }
    }

    private static void Convolve<TSample>(PixelStorage storage, ConvolutionPlan plan, CancellationToken cancellationToken)
        where TSample : unmanaged
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Every band reads its halos from the original pixels: load them all before the first row is rewritten
        var workers = plan.Workers;
        for (var band = 0; band < workers; band++)
        {
            LoadHalos<TSample>(storage, plan, band);
        }

        var observer = TestBandObserver;
        if (workers == 1)
        {
            ConvolveBand<TSample>(storage, plan, 0, observer, cancellationToken);
            return;
        }

        RowBands.RunParallel(workers, band => ConvolveBand<TSample>(storage, plan, band, observer, cancellationToken));
    }

    /// <summary>Loads the rows that a band reads above its first row and below its last one (absent rows contribute nothing and are not loaded).</summary>
    private static void LoadHalos<TSample>(PixelStorage storage, ConvolutionPlan plan, int band)
        where TSample : unmanaged
    {
        var scratch = plan.GetScratch(band);
        var start = plan.GetBandStart(band);
        var end = plan.GetBandStart(band + 1);
        var centerY = plan.CenterY;
        for (var i = 0; i < centerY; i++)
        {
            var above = plan.MapRow(start - centerY + i);
            if (above >= 0)
            {
                LoadRow<TSample>(storage, above, plan, scratch.GetTopHalo(i));
            }

            var below = plan.MapRow(end + i);
            if (below >= 0)
            {
                LoadRow<TSample>(storage, below, plan, scratch.GetBottomHalo(i));
            }
        }
    }

    private static void ConvolveBand<TSample>(PixelStorage storage, ConvolutionPlan plan, int band, Action<int, bool>? observer, CancellationToken cancellationToken)
        where TSample : unmanaged
    {
        observer?.Invoke(band, true);
        var scratch = plan.GetScratch(band);
        var start = plan.GetBandStart(band);
        var end = plan.GetBandStart(band + 1);
        var weights = plan.Weights;
        var kernelWidth = plan.KernelWidth;
        var kernelHeight = plan.KernelHeight;
        var centerY = plan.CenterY;
        var channels = plan.Channels;
        var accumulator = scratch.Accumulator;
        var next = start; // the next row of the band to load into the ring
        for (var y = start; y < end; y++)
        {
            if (y % ProcessingKernels.RowsPerCancellationCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            // The ring holds the rows of the band among [y - centerY, y + centerY]. A row is loaded before row y is
            // written, and the band has only written rows above y: the ring rows are original pixels.
            var last = Math.Min(y + centerY, end - 1);
            for (; next <= last; next++)
            {
                LoadRow<TSample>(storage, next, plan, scratch.GetRingRow(next));
            }

            var empty = true;
            for (var j = 0; j < kernelHeight; j++)
            {
                var source = y + j - centerY;
                ReadOnlySpan<double> row;
                if (source < start)
                {
                    if (plan.MapRow(source) < 0)
                        continue;

                    row = scratch.GetTopHalo(source - (start - centerY));
                }
                else if (source >= end)
                {
                    if (plan.MapRow(source) < 0)
                        continue;

                    row = scratch.GetBottomHalo(source - end);
                }
                else
                {
                    row = scratch.GetRingRow(source);
                }

                var kernelRow = weights.Slice(j * kernelWidth, kernelWidth);
                for (var i = 0; i < kernelWidth; i++)
                {
                    var weight = kernelRow[i];
                    if (weight == 0)
                        continue;

                    // The padded row starts centerX pixels left of the image: its slice at i is the row shifted by i - centerX
                    var shifted = row.Slice(i * channels, accumulator.Length);
                    if (empty)
                    {
                        // 0 + w0 x0 written directly (no separate clearing pass)
                        Resampler.SetScaled(accumulator, shifted, weight);
                        empty = false;
                    }
                    else
                    {
                        Resampler.AddScaled(accumulator, shifted, weight);
                    }
                }
            }

            if (empty)
            {
                accumulator.Clear();
            }

            WorkingSamples.Store(accumulator, GetSamples<TSample>(storage, y), plan.Alpha, plan.IsLinear, plan.MaxValue);
        }

        observer?.Invoke(band, false);
    }

    /// <summary>
    /// Loads image row <paramref name="y"/> as working samples in the middle of <paramref name="padded"/>, then fills the
    /// pixels read left and right of the image according to the edge mode.
    /// </summary>
    private static void LoadRow<TSample>(PixelStorage storage, int y, ConvolutionPlan plan, Span<double> padded)
        where TSample : unmanaged
    {
        var channels = plan.Channels;
        var centerX = plan.CenterX;
        var width = plan.Size.Width;
        WorkingSamples.Load<TSample>(GetSamples<TSample>(storage, y), padded.Slice(centerX * channels, width * channels), plan.Alpha, plan.IsLinear);
        for (var i = 0; i < centerX; i++)
        {
            FillPadding(padded, i, plan.MapColumn(i - centerX), centerX, channels);
            FillPadding(padded, centerX + width + i, plan.MapColumn(width + i), centerX, channels);
        }
    }

    private static void FillPadding(Span<double> padded, int pixel, int sourceColumn, int centerX, int channels)
    {
        var destination = padded.Slice(pixel * channels, channels);
        if (sourceColumn < 0)
        {
            destination.Clear();
        }
        else
        {
            padded.Slice((centerX + sourceColumn) * channels, channels).CopyTo(destination);
        }
    }

    private static Span<TSample> GetSamples<TSample>(PixelStorage storage, int y)
        where TSample : unmanaged
        => unsafe(MemoryMarshal.Cast<byte, TSample>(storage.GetRowSpanCore(y)));
}
