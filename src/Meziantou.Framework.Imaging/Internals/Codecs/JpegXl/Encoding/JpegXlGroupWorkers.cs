using System.Runtime.ExceptionServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The groups of a JPEG XL frame coded by several workers (<see cref="ImageConfiguration.MaxDegreeOfParallelism"/>): the
/// groups do not depend on each other, so each worker takes the next one that is left, with a state of its own (its
/// working memory). What a group produces never depends on the worker that coded it.
/// </summary>
internal static class JpegXlGroupWorkers
{
    /// <summary>The smallest number of pixels of a frame coded by several workers: smaller frames are not worth a thread.</summary>
    internal const long MinPixelsForParallelism = 64 * 1024;

    /// <summary>Gets the number of workers for the groups of a frame: at most one per group, and one for a small frame.</summary>
    public static int GetWorkerCount(int maxDegreeOfParallelism, int groupCount, long pixelCount)
    {
        if (maxDegreeOfParallelism <= 1 || groupCount <= 1 || pixelCount < MinPixelsForParallelism)
            return 1;

        return Math.Min(maxDegreeOfParallelism, groupCount);
    }

    /// <summary>Runs <paramref name="body"/> for every group, on up to <paramref name="workerCount"/> threads, without a state per worker.</summary>
    public static void Run(int workerCount, int groupCount, Action<int> body)
        => Run(workerCount, groupCount, static () => NoState.Instance, (_, group) => body(group));

    /// <summary>
    /// Runs <paramref name="body"/> for every group, on up to <paramref name="workerCount"/> threads (the calling thread
    /// included). Each thread creates its state the first time it takes a group; every state is disposed at the end. The
    /// error is the one of a sequential run: the one of the first group that fails. Once a group fails, the groups after it
    /// are skipped and the ones before it still run, since one of them may fail too.
    /// </summary>
    public static void Run<TState>(int workerCount, int groupCount, Func<TState> createState, Action<TState, int> body)
        where TState : IDisposable
    {
        var states = new List<TState>();
        var failedGroup = int.MaxValue;
        Exception? failure = null;
        var failureLock = new Lock();
        try
        {
            Parallel.For(
                0,
                groupCount,
                new ParallelOptions { MaxDegreeOfParallelism = workerCount },
                () =>
                {
                    var state = createState();
                    lock (states)
                    {
                        states.Add(state);
                    }

                    return state;
                },
                (group, _, state) =>
                {
                    if (group > Volatile.Read(ref failedGroup))
                        return state;

                    try
                    {
                        body(state, group);
                    }
                    catch (Exception exception)
                    {
                        lock (failureLock)
                        {
                            if (group < failedGroup)
                            {
                                failure = exception;
                                Volatile.Write(ref failedGroup, group);
                            }
                        }
                    }

                    return state;
                },
                _ => { });
        }
        catch (AggregateException exception)
        {
            // A state that could not be created
            failure ??= exception.InnerExceptions.FirstOrDefault(e => e is OperationCanceledException) ?? exception.InnerExceptions[0];
        }
        finally
        {
            foreach (var state in states)
            {
                state.Dispose();
            }
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private sealed class NoState : IDisposable
    {
        public static NoState Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
