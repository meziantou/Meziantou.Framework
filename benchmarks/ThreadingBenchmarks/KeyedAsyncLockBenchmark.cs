using BenchmarkDotNet.Attributes;
using Meziantou.Framework.Threading;

namespace ThreadingBenchmarks;

[MemoryDiagnoser]
public class KeyedAsyncLockBenchmark
{
    private const int OperationsPerInvoke = 64 * 1024;

    private readonly KeyedAsyncLock<int> _locks = new();
    private int[] _keys = [];

    /// <summary>Number of distinct keys cycled through. 1 models a hot key, larger values model higher cardinality.</summary>
    [Params(1, 64)]
    public int KeyCount { get; set; }

    /// <summary>Number of tasks acquiring concurrently. Each task works on its own disjoint key range.</summary>
    [Params(1, 8)]
    public int ThreadCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _keys = [.. Enumerable.Range(0, KeyCount * ThreadCount)];
    }

    [Benchmark(OperationsPerInvoke = OperationsPerInvoke)]
    public async Task AcquireAsync()
    {
        if (ThreadCount is 1)
        {
            await RunAsync(threadIndex: 0);
            return;
        }

        var tasks = new Task[ThreadCount];
        for (var i = 0; i < ThreadCount; i++)
        {
            var threadIndex = i;
            tasks[i] = Task.Run(() => RunAsync(threadIndex));
        }

        await Task.WhenAll(tasks);
    }

    private async Task RunAsync(int threadIndex)
    {
        var offset = threadIndex * KeyCount;
        for (var i = 0; i < OperationsPerInvoke / ThreadCount; i++)
        {
            using (await _locks.LockAsync(_keys[offset + (i % KeyCount)]))
            {
            }
        }
    }
}
