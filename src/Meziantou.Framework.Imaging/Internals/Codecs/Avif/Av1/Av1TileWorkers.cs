using System.Runtime.ExceptionServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The tiles of an AV1 frame coded by several workers (<see cref="ImageConfiguration.MaxDegreeOfParallelism"/>): a tile
/// does not depend on the other tiles of its frame (AV1 specification, section 7.3: its contexts and its distributions
/// start again, and the blocks outside it are not available), so each worker takes the next tile that is left, with a
/// state of its own that the caller rents before the tiles start. What a tile produces never depends on the worker that
/// coded it.
/// </summary>
internal static class Av1TileWorkers
{
    private static readonly AsyncLocal<bool> TestReverseOrderValue = new();

    /// <summary>
    /// Gets or sets a value indicating whether the tiles coded on the current asynchronous flow run one after the other on
    /// the calling thread, from the last one to the first one, each by the worker of its index modulo the number of
    /// workers: whatever the scheduling of a test machine, the tests check that a tile depends neither on the tiles before
    /// it nor on what a worker keeps from its previous tile, and that the error is still the one of the first failing tile.
    /// </summary>
    internal static bool TestReverseOrder
    {
        get => TestReverseOrderValue.Value;
        set => TestReverseOrderValue.Value = value;
    }

    /// <summary>
    /// Gets the number of workers of <paramref name="tileCount"/> tiles of a frame of <paramref name="pixelCount"/> pixels:
    /// one for a single tile or for fewer than <see cref="RowBands.MinPixelsForParallelism"/> pixels (unless
    /// <see cref="Av1PostFilter.TestIgnoreMinimumSize"/> is set), at most one per tile otherwise.
    /// </summary>
    public static int GetWorkerCount(int maxDegreeOfParallelism, int tileCount, long pixelCount)
    {
        if (maxDegreeOfParallelism <= 1 || tileCount < 2)
            return 1;

        if (!Av1PostFilter.TestIgnoreMinimumSize && pixelCount < RowBands.MinPixelsForParallelism)
            return 1;

        return Math.Min(maxDegreeOfParallelism, tileCount);
    }

    /// <summary>
    /// Runs <paramref name="body"/> (the worker, from 0, then the tile) for the tiles 0 to <paramref name="tileCount"/> - 1,
    /// on <paramref name="workers"/> workers, the calling thread included. The error is the one of a sequential run: a
    /// cancellation first, else the error of the first tile that fails. Once a tile fails, the tiles after it are skipped;
    /// the tiles before it were taken already and still run, since one of them may fail too.
    /// </summary>
    public static void Run(int workers, int tileCount, Action<int, int> body)
    {
        var failures = new Exception?[tileCount];
        var firstFailure = tileCount;
        var next = -1;
        if (TestReverseOrder)
        {
            for (var tile = tileCount - 1; tile >= 0; tile--)
            {
                try
                {
                    body(tile % workers, tile);
                }
                catch (Exception ex)
                {
                    failures[tile] = ex;
                }
            }

            ThrowFirstFailure(failures);
            return;
        }

        RowBands.RunParallel(workers, worker =>
        {
            while (true)
            {
                var tile = Interlocked.Increment(ref next);
                if (tile >= tileCount || tile > Volatile.Read(ref firstFailure))
                    return;

                try
                {
                    body(worker, tile);
                }
                catch (Exception ex)
                {
                    failures[tile] = ex;
                    int current;
                    while (tile < (current = Volatile.Read(ref firstFailure)) && Interlocked.CompareExchange(ref firstFailure, tile, current) != current)
                    {
                    }
                }
            }
        });

        ThrowFirstFailure(failures);
    }

    private static void ThrowFirstFailure(Exception?[] failures)
    {
        var failure = Array.Find(failures, e => e is OperationCanceledException) ?? Array.Find(failures, e => e is not null);
        if (failure is not null)
            ExceptionDispatchInfo.Throw(failure);
    }
}
