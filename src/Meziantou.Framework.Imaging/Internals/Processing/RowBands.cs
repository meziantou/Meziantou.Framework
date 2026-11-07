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
    /// The number of rows of the bands of the floating-point sums (<see cref="Sum"/>): a constant, so that the sums are made
    /// in the same order whatever the number of workers.
    /// </summary>
    internal const int SumBandRows = 64;

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

    /// <summary>Runs <paramref name="rows"/> on the rows of a plane: once on all of them with one worker, else once per band.</summary>
    internal static void For(Size size, int maxDegreeOfParallelism, Action<int, int> rows)
    {
        var workers = GetWorkerCount(size, maxDegreeOfParallelism);
        if (workers is 1)
        {
            rows(0, size.Height);
            return;
        }

        RunParallel(workers, band => rows(GetBandStart(size.Height, band, workers), GetBandStart(size.Height, band + 1, workers)));
    }

    /// <summary>
    /// Computes floating-point sums over the rows of a plane: <paramref name="band"/> adds the values of a band of
    /// <see cref="SumBandRows"/> rows (the last one may be shorter) to sums that start at zero, and the sums of the bands are
    /// added in the order of the bands. The workers take contiguous groups of bands, so the result does not depend on their
    /// number; a plane of at most <see cref="SumBandRows"/> rows gets the sums of one sequential loop.
    /// </summary>
    /// <param name="size">The size of the plane.</param>
    /// <param name="maxDegreeOfParallelism">The largest number of workers.</param>
    /// <param name="sums">Receives the sums.</param>
    /// <param name="band">Adds the values of the rows from its first argument (inclusive) to its second one (exclusive).</param>
    internal static void Sum(Size size, int maxDegreeOfParallelism, Span<double> sums, BandSum band)
    {
        var count = sums.Length;
        var bands = (size.Height + SumBandRows - 1) / SumBandRows;
        var bandSums = new double[bands * count];
        var workers = Math.Min(GetWorkerCount(size, maxDegreeOfParallelism), bands);
        if (workers is 1)
        {
            SumBands(0, bands);
        }
        else
        {
            RunParallel(workers, worker => SumBands(GetBandStart(bands, worker, workers), GetBandStart(bands, worker + 1, workers)));
        }

        sums.Clear();
        for (var i = 0; i < bandSums.Length; i += count)
        {
            for (var k = 0; k < count; k++)
            {
                sums[k] += bandSums[i + k];
            }
        }

        void SumBands(int first, int end)
        {
            for (var b = first; b < end; b++)
            {
                band(b * SumBandRows, Math.Min(size.Height, (b + 1) * SumBandRows), bandSums.AsSpan(b * count, count));
            }
        }
    }

    /// <summary>
    /// Runs one band per worker, like <see cref="RunParallel"/>, but every band runs to its end or its failure, and the
    /// exception is the one of the first band that failed (a cancellation first): the one of a sequential run, for bands
    /// whose failures depend on the data they read (such as a sample without an 8-bit value).
    /// </summary>
    internal static void RunParallelInBandOrder(int workers, Action<int> band)
    {
        var failures = new Exception?[workers];
        RunParallel(workers, b =>
        {
            try
            {
                band(b);
            }
            catch (Exception exception)
            {
                failures[b] = exception;
            }
        });

        var failure = Array.Find(failures, e => e is OperationCanceledException) ?? Array.Find(failures, e => e is not null);
        if (failure is not null)
        {
            ExceptionDispatchInfo.Throw(failure);
        }
    }

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

    /// <summary>Adds the values of the rows <c>[start, end)</c> to <paramref name="sums"/>, which start at zero.</summary>
    internal delegate void BandSum(int start, int end, Span<double> sums);
}
