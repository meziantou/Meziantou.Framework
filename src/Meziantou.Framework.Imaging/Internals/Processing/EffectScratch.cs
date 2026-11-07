using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The row bands of the pixelation and the oil painting of <see cref="EffectKernels"/>, and the scratch of each worker,
/// rented from the scope of the image before any pixel changes. One instance serves every frame of an image.
/// </summary>
/// <remarks>
/// <para>
/// The bands of the pixelation start on the first row of a block, so a block is never split between two workers. Those of
/// the oil painting are at least as tall as the window, and each worker keeps a copy of the rows it reads above and below
/// its band (the halos), taken before any row is rewritten: every window reads the original pixels.
/// </para>
/// <para>
/// The number of workers is that of <see cref="RowBands.GetWorkerCount"/>, at most one per block row (pixelation) or one per
/// window height (oil painting). Every value is computed as by one worker, so the result does not depend on it.
/// </para>
/// </remarks>
internal sealed class EffectScratch : IDisposable
{
    private readonly int _bandUnit;
    private PooledBuffer?[] _doubles;
    private PooledBuffer?[] _ints;
    private PooledBuffer?[] _halos;

    private EffectScratch(Size size, int workers, int bandUnit, int haloRows)
    {
        Size = size;
        Workers = workers;
        HaloRows = haloRows;
        _bandUnit = bandUnit;
        _doubles = new PooledBuffer?[workers];
        _ints = new PooledBuffer?[workers];
        _halos = new PooledBuffer?[workers];
    }

    /// <summary>Gets the size of the frames.</summary>
    public Size Size { get; }

    /// <summary>Gets the number of workers (and row bands) of each frame.</summary>
    public int Workers { get; }

    /// <summary>Gets the number of rows of each halo (above and below a band): the radius of the oil painting with several workers, else 0.</summary>
    public int HaloRows { get; }

    /// <summary>Rents the scratch of the pixelation (<see cref="EffectKernels.Pixelate{TPixel}"/>) of frames of a size.</summary>
    /// <exception cref="ImageResourceLimitException">The scratch exceeds the allocation limit.</exception>
    public static EffectScratch ForPixelate(AllocationScope scope, PixelFormat format, Size size, int blockSize, int maxDegreeOfParallelism)
    {
        var blockRows = (int)(((long)size.Height + blockSize - 1) / blockSize);
        var workers = Math.Min(RowBands.GetWorkerCount(size, maxDegreeOfParallelism), blockRows);
        var scratch = new EffectScratch(size, workers, blockSize, haloRows: 0);
        return scratch.Rent(scope, EffectKernels.GetPixelateScratchLength(format, size.Width, blockSize), ints: 0, haloBytes: 0);
    }

    /// <summary>Rents the scratch of the oil painting (<see cref="EffectKernels.OilPaint{TPixel}"/>) of frames of a size.</summary>
    /// <exception cref="ImageResourceLimitException">The scratch exceeds the allocation limit.</exception>
    public static EffectScratch ForOilPaint(AllocationScope scope, PixelFormat format, Size size, int radius, int levels, int maxDegreeOfParallelism)
    {
        // A band is at least as tall as the window: the halos and the rings of the workers stay below a copy of the frame
        var diameter = (2 * radius) + 1;
        var workers = Math.Min(RowBands.GetWorkerCount(size, maxDegreeOfParallelism), Math.Max(1, size.Height / diameter));
        var haloRows = workers > 1 ? radius : 0;
        var scratch = new EffectScratch(size, workers, bandUnit: 1, haloRows);
        var haloBytes = 2L * haloRows * size.Width * PixelFormats.GetBytesPerPixel(format);
        return scratch.Rent(scope, EffectKernels.GetOilPaintScratchLength(format, size.Width, radius, levels), EffectKernels.GetOilPaintLevelsLength(size.Width, radius, levels), haloBytes);
    }

    /// <summary>Gets the first row of a band (<paramref name="band"/> = <see cref="Workers"/> gives the height).</summary>
    public int GetBandStart(int band)
    {
        var units = (int)(((long)Size.Height + _bandUnit - 1) / _bandUnit);
        return (int)Math.Min(Size.Height, (long)RowBands.GetBandStart(units, band, Workers) * _bandUnit);
    }

    /// <summary>Gets the doubles of a worker.</summary>
    public Span<double> GetDoubles(int worker) => unsafe(MemoryMarshal.Cast<byte, double>(GetBuffer(_doubles, worker)));

    /// <summary>Gets the integers of a worker.</summary>
    public Span<int> GetInts(int worker) => unsafe(MemoryMarshal.Cast<byte, int>(GetBuffer(_ints, worker)));

    /// <summary>Gets the halos of a worker: <see cref="HaloRows"/> rows above its band, then as many below it, in the stored format.</summary>
    public Span<byte> GetHalo(int worker) => GetBuffer(_halos, worker);

    public void Dispose()
    {
        DisposeAll(_doubles);
        DisposeAll(_ints);
        DisposeAll(_halos);
        _doubles = [];
        _ints = [];
        _halos = [];

        static void DisposeAll(PooledBuffer?[] buffers)
        {
            foreach (var buffer in buffers)
            {
                buffer?.Dispose();
            }
        }
    }

    private static Span<byte> GetBuffer(PooledBuffer?[] buffers, int worker) => worker < buffers.Length && buffers[worker] is { } buffer ? buffer.Span : throw new ObjectDisposedException(nameof(EffectScratch));

    private EffectScratch Rent(AllocationScope scope, long doubles, long ints, long haloBytes)
    {
        try
        {
            for (var worker = 0; worker < Workers; worker++)
            {
                _doubles[worker] = ResampleWeights.Rent<double>(scope, doubles);
                _ints[worker] = ResampleWeights.Rent<int>(scope, ints);
                _halos[worker] = ResampleWeights.Rent<byte>(scope, haloBytes);
            }
        }
        catch
        {
            Dispose();
            throw;
        }

        return this;
    }
}
