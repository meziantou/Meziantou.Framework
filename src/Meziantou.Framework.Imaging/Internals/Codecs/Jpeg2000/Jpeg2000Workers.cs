using System.Runtime.ExceptionServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The parallelism of the JPEG 2000 codec (<see cref="ImageConfiguration.MaxDegreeOfParallelism"/>): the code-blocks of a
/// tile are decoded and coded by several workers, and the wavelet transformations, the component transformations and the
/// level shifts are split by rows and by strips of columns. The units of work never share a sample, each worker has its
/// own state, rented on the calling thread before the work starts, and the result and the errors are those of one worker.
/// </summary>
internal static class Jpeg2000Workers
{
    private static readonly AsyncLocal<bool> TestIgnoreMinimumSizeValue = new();

    /// <summary>
    /// Gets or sets a value indicating whether the tiles decoded or coded on the current asynchronous flow use the workers
    /// they are given whatever their size (at most one per unit of work), instead of the minimum size of
    /// <see cref="RowBands.MinPixelsForParallelism"/> pixels. Tests decode small files by several workers.
    /// </summary>
    internal static bool TestIgnoreMinimumSize
    {
        get => TestIgnoreMinimumSizeValue.Value;
        set => TestIgnoreMinimumSizeValue.Value = value;
    }

    /// <summary>
    /// Gets the number of workers of a tile: one below <see cref="RowBands.MinPixelsForParallelism"/> pixels or for a single
    /// unit of work, else at most <paramref name="maxDegreeOfParallelism"/> and one per unit.
    /// </summary>
    /// <param name="maxDegreeOfParallelism">The largest number of workers.</param>
    /// <param name="pixelCount">The number of pixels of the tile.</param>
    /// <param name="unitCount">The number of units of work of the tile.</param>
    public static int GetWorkerCount(int maxDegreeOfParallelism, long pixelCount, int unitCount)
    {
        if (maxDegreeOfParallelism <= 1 || unitCount < 2 || (pixelCount < RowBands.MinPixelsForParallelism && !TestIgnoreMinimumSize))
            return 1;

        return Math.Min(maxDegreeOfParallelism, unitCount);
    }

    /// <summary>
    /// Gets the number of bands that split <paramref name="length"/> rows (or strips) of a pass of <paramref name="samples"/>
    /// samples: one for a pass smaller than <see cref="RowBands.MinPixelsForParallelism"/> samples, else at most
    /// <paramref name="workers"/> bands of at least <paramref name="minimumPerBand"/> rows.
    /// </summary>
    public static int GetBandCount(int workers, long samples, int length, int minimumPerBand)
    {
        if (workers <= 1 || length < 2)
            return 1;

        if (TestIgnoreMinimumSize)
            return Math.Min(workers, length);

        if (samples < RowBands.MinPixelsForParallelism)
            return 1;

        return Math.Clamp(length / minimumPerBand, 1, workers);
    }

    /// <summary>
    /// Runs <paramref name="body"/> on every unit of work in <c>[0, unitCount)</c>, on <paramref name="workers"/> workers
    /// (the calling thread included), each taking the next unit that is left: <paramref name="body"/> receives the index of
    /// the worker, which owns its state, and the unit. The error is the one of a sequential run: a cancellation first, else
    /// the error of the first unit, in order, that fails. Once a unit fails, the units after it are skipped and the ones
    /// before it still run, since one of them may fail too.
    /// </summary>
    public static void Run(int workers, int unitCount, Action<int, int> body)
    {
        var next = -1;
        var state = new FailureState();
        RowBands.RunParallel(workers, worker =>
        {
            while (true)
            {
                var unit = Interlocked.Increment(ref next);
                if (unit >= unitCount || state.ShouldSkip(unit))
                    return;

                try
                {
                    body(worker, unit);
                }
                catch (Exception exception)
                {
                    state.Record(unit, exception);
                }
            }
        });

        state.ThrowIfFailed();
    }

    /// <summary>
    /// Runs <paramref name="body"/> on every unit of work in <c>[0, unitCount)</c>, on <paramref name="workers"/> workers
    /// (the calling thread included): the worker <c>w</c> runs the units <c>w</c>, <c>w + workers</c>, <c>w + 2 workers</c>
    /// and so on, in this order, so that what each worker does only depends on the number of workers. The errors are those
    /// of <see cref="Run"/>.
    /// </summary>
    public static void RunInterleaved(int workers, int unitCount, Action<int, int> body)
    {
        var state = new FailureState();
        RowBands.RunParallel(workers, worker =>
        {
            for (var unit = worker; unit < unitCount; unit += workers)
            {
                if (state.ShouldSkip(unit))
                    return;

                try
                {
                    body(worker, unit);
                }
                catch (Exception exception)
                {
                    state.Record(unit, exception);
                    return;
                }
            }
        });

        state.ThrowIfFailed();
    }

    /// <summary>Runs <paramref name="band"/> once per band, at most <paramref name="bands"/> at once; nothing in parallel for one band.</summary>
    public static void RunBands(int bands, Action<int> band)
    {
        if (bands is 1)
        {
            band(0);
            return;
        }

        RowBands.RunParallel(bands, band);
    }

    /// <summary>Gets the first item of a band: <paramref name="bands"/> contiguous ranges split <paramref name="length"/> items.</summary>
    public static int GetBandStart(int length, int band, int bands) => RowBands.GetBandStart(length, band, bands);

    // The first failure of the units, in their order, a cancellation first
    private sealed class FailureState
    {
        private readonly Lock _lock = new();
        private int _failedUnit = int.MaxValue;
        private Exception? _failure;
        private Exception? _cancellation;

        public bool ShouldSkip(int unit) => unit > Volatile.Read(ref _failedUnit);

        public void Record(int unit, Exception exception)
        {
            lock (_lock)
            {
                if (exception is OperationCanceledException)
                {
                    // Nothing else matters once the operation is canceled: every unit is skipped
                    _cancellation ??= exception;
                    Volatile.Write(ref _failedUnit, -1);
                }
                else if (unit < _failedUnit)
                {
                    _failure = exception;
                    Volatile.Write(ref _failedUnit, unit);
                }
            }
        }

        public void ThrowIfFailed()
        {
            var failure = _cancellation ?? _failure;
            if (failure is not null)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }
    }
}
