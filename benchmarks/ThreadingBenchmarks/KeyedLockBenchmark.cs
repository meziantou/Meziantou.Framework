using BenchmarkDotNet.Attributes;
using Meziantou.Framework.Threading;

namespace ThreadingBenchmarks;

[MemoryDiagnoser]
public class KeyedLockBenchmark
{
    private const int OperationsPerInvoke = 64 * 1024;

    private readonly KeyedLock<int> _locks = new();
    private int[] _keys = [];

    /// <summary>Number of distinct keys cycled through. 1 models a hot key, larger values model higher cardinality.</summary>
    [Params(1, 64)]
    public int KeyCount { get; set; }

    /// <summary>Number of threads acquiring concurrently. Each thread works on its own disjoint key range.</summary>
    [Params(1, 8)]
    public int ThreadCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _keys = [.. Enumerable.Range(0, KeyCount * ThreadCount)];
    }

    [Benchmark(OperationsPerInvoke = OperationsPerInvoke)]
    public void Acquire()
    {
        if (ThreadCount is 1)
        {
            Run(threadIndex: 0);
            return;
        }

        var tasks = new Task[ThreadCount];
        for (var i = 0; i < ThreadCount; i++)
        {
            var threadIndex = i;
            tasks[i] = Task.Run(() => Run(threadIndex));
        }

        Task.WaitAll(tasks);
    }

    private void Run(int threadIndex)
    {
        var offset = threadIndex * KeyCount;
        for (var i = 0; i < OperationsPerInvoke / ThreadCount; i++)
        {
            using (_locks.Lock(_keys[offset + (i % KeyCount)]))
            {
            }
        }
    }
}
