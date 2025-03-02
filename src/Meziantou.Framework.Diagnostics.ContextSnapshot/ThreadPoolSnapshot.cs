namespace Meziantou.Framework.Diagnostics.ContextSnapshot;

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

    public int ThreadCount { get; }
    public int AvailableWorkerThreadCount { get; }
    public int AvailableCompletionPortThreadCount { get; }
    public int MinWorkerThreadCount { get; }
    public int MaxWorkerThreadCount { get; }
    public int MinCompletionPortThreadCount { get; }
    public int MaxCompletionPortThreadCount { get; }
}
