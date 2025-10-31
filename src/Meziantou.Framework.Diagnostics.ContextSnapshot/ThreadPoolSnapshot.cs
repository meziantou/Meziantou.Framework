namespace Meziantou.Framework.Diagnostics.ContextSnapshot;

/// <summary>Represents a snapshot of thread pool information at a specific point in time.</summary>
public sealed class ThreadPoolSnapshot
{
    internal ThreadPoolSnapshot()
    {
        ThreadPool.GetMinThreads(out var minWorkerThreads, out var minCompletionPortThreads);
        ThreadPool.GetMaxThreads(out var maxWorkerThreads, out var maxCompletionPortThreads);
        ThreadPool.GetAvailableThreads(out var availableWorkerThreads, out var availableCompletionPortThreads);

        ThreadCount = ThreadPool.ThreadCount;
        AvailableWorkerThreadCount = availableWorkerThreads;
        AvailableCompletionPortThreadCount = availableCompletionPortThreads;
        MinWorkerThreadCount = minWorkerThreads;
        MaxWorkerThreadCount = maxWorkerThreads;
        MinCompletionPortThreadCount = minCompletionPortThreads;
        MaxCompletionPortThreadCount = maxCompletionPortThreads;
    }

    /// <summary>Gets the total number of thread pool threads.</summary>
    public int ThreadCount { get; }
    /// <summary>Gets the number of available worker threads in the thread pool.</summary>
    public int AvailableWorkerThreadCount { get; }
    /// <summary>Gets the number of available completion port threads in the thread pool.</summary>
    public int AvailableCompletionPortThreadCount { get; }
    /// <summary>Gets the minimum number of worker threads in the thread pool.</summary>
    public int MinWorkerThreadCount { get; }
    /// <summary>Gets the maximum number of worker threads in the thread pool.</summary>
    public int MaxWorkerThreadCount { get; }
    /// <summary>Gets the minimum number of completion port threads in the thread pool.</summary>
    public int MinCompletionPortThreadCount { get; }
    /// <summary>Gets the maximum number of completion port threads in the thread pool.</summary>
    public int MaxCompletionPortThreadCount { get; }
}
