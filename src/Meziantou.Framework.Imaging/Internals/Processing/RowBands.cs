using System.Runtime.ExceptionServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The bounded parallelism of the row-filtering operations: the rows of a large frame are split
/// into contiguous bands, one per worker, and the result never depends on the number of workers.
/// </summary>
internal static class RowBands
{
    /// <summary>The smallest number of rows per band: smaller bands are not worth a worker.</summary>
    internal const int MinRowsPerBand = 32;

    /// <summary>The smallest number of pixels of a frame processed by several workers.</summary>
    internal const long MinPixelsForParallelism = 64 * 1024;

    /// <summary>
    /// Gets the number of workers: at most <paramref name="maxDegreeOfParallelism"/>, at least <see cref="MinRowsPerBand"/>
    /// rows per band, and a single worker below <see cref="MinPixelsForParallelism"/> pixels.
    /// </summary>
    internal static int GetWorkerCount(Size size, int maxDegreeOfParallelism)
    {
        if (maxDegreeOfParallelism <= 1 || (long)size.Width * size.Height < MinPixelsForParallelism)
            return 1;

        return Math.Clamp(size.Height / MinRowsPerBand, 1, maxDegreeOfParallelism);
    }

    /// <summary>Gets the first row of a band (the bands split <paramref name="height"/> rows into <paramref name="workers"/> contiguous ranges).</summary>
    internal static int GetBandStart(int height, int band, int workers) => (int)((long)height * band / workers);

    /// <summary>
    /// Runs one band per worker, at most <paramref name="workers"/> at once (the calling thread included). The workers run in
    /// the caller's execution context (the test hooks flow with it). Exceptions are those of a sequential run:
    /// cancellation first, else the first failure, rethrown unwrapped.
    /// </summary>
    internal static void RunParallel(int workers, Action<int> band)
    {
        try
        {
            Parallel.For(0, workers, new ParallelOptions { MaxDegreeOfParallelism = workers }, band);
        }
        catch (AggregateException exception)
        {
            var inner = exception.InnerExceptions.FirstOrDefault(e => e is OperationCanceledException) ?? exception.InnerExceptions[0];
            ExceptionDispatchInfo.Capture(inner).Throw();
            throw;
        }
    }
}
