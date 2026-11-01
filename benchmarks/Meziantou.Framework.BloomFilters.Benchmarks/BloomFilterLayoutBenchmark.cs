using BenchmarkDotNet.Attributes;

namespace Meziantou.Framework.BloomFilters.Benchmarks;

/// <summary>
/// Compares the classic layout with the blocked prototypes on filters filled to their design capacity, with random
/// keys. Each invocation streams through a new window of keys so the large filter is never served from the cache.
/// </summary>
[MemoryDiagnoser]
public class BloomFilterLayoutBenchmark
{
    private const int OperationsPerInvoke = 1 << 18;

    private LayoutScenario _scenario = null!;
    private ulong[] _mixPattern = null!;
    private long _cursor;

    [Params(FilterMemory.Small512KiB, FilterMemory.Large128MiB)]
    public FilterMemory Memory { get; set; }

    [Params(FilterLayout.Classic, FilterLayout.BlockedEqualMemory, FilterLayout.BlockedEqualFpr)]
    public FilterLayout Layout { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _scenario = LayoutScenario.CreateFilled(Memory, Layout);
        _mixPattern = new ulong[OperationsPerInvoke / 64];
        for (var i = 0; i < _mixPattern.Length; i++)
        {
            _mixPattern[i] = LayoutScenario.Key(-1 - i);
        }
    }

    [GlobalCleanup]
    public void Cleanup() => _scenario = null!;

    // Re-adding present keys keeps the occupancy at its target across invocations while doing the same memory work
    // (the atomic OR still needs the cache line in exclusive state).
    [Benchmark(OperationsPerInvoke = OperationsPerInvoke)]
    public void Add() => Run(new AddOperation(), NextWindow(), 0);

    [Benchmark(OperationsPerInvoke = OperationsPerInvoke)]
    public int MayContain_Present() => Run(new QueryOperation(), NextWindow(), 0);

    [Benchmark(OperationsPerInvoke = OperationsPerInvoke)]
    public int MayContain_Absent() => Run(new QueryOperation(), _scenario.ItemCount + NextWindow(), 0);

    [Benchmark(OperationsPerInvoke = OperationsPerInvoke)]
    public int MayContain_Mixed50() => Run(new MixedQueryOperation(_mixPattern, _scenario.ItemCount), NextWindow(), 0);

    [Benchmark(OperationsPerInvoke = OperationsPerInvoke)]
    public void Add_Concurrent()
    {
        var start = NextWindow();
        var threadCount = Environment.ProcessorCount;
        var chunk = OperationsPerInvoke / threadCount;
        Parallel.For(0, threadCount, new ParallelOptions { MaxDegreeOfParallelism = threadCount }, thread => Run(new AddOperation(), (start + (thread * chunk)) % _scenario.ItemCount, chunk));
    }

    [Benchmark]
    public object Construct() => LayoutScenario.CreateEmpty(Memory, Layout);

    private long NextWindow()
    {
        var result = _cursor;
        _cursor = (_cursor + OperationsPerInvoke) % (_scenario.ItemCount - OperationsPerInvoke);
        return result;
    }

    private int Run<TOperation>(TOperation operation, long start, int count)
        where TOperation : struct, IOperation
    {
        if (count is 0)
        {
            count = OperationsPerInvoke;
        }

        return _scenario.Classic is not null
            ? Run(new ClassicAdapter(_scenario.Classic), operation, start, count)
            : Run(new BlockedAdapter(_scenario.Blocked!), operation, start, count);
    }

    private static int Run<TFilter, TOperation>(TFilter filter, TOperation operation, long start, int count)
        where TFilter : struct, IFilterAdapter
        where TOperation : struct, IOperation
    {
        var result = 0;
        for (var i = 0; i < count; i++)
        {
            result += operation.Execute(filter, start, i);
        }

        return result;
    }

    private interface IOperation
    {
        int Execute<TFilter>(TFilter filter, long start, int index)
            where TFilter : struct, IFilterAdapter;
    }

    private readonly struct AddOperation : IOperation
    {
        public int Execute<TFilter>(TFilter filter, long start, int index)
            where TFilter : struct, IFilterAdapter
        {
            filter.Add(LayoutScenario.Key(start + index));
            return 0;
        }
    }

    private readonly struct QueryOperation : IOperation
    {
        public int Execute<TFilter>(TFilter filter, long start, int index)
            where TFilter : struct, IFilterAdapter
        {
            return filter.MayContain(LayoutScenario.Key(start + index)) ? 1 : 0;
        }
    }

    // Half of the keys are present, in a random order, so the branch predictor cannot learn the answer.
    private readonly struct MixedQueryOperation(ulong[] pattern, long itemCount) : IOperation
    {
        public int Execute<TFilter>(TFilter filter, long start, int index)
            where TFilter : struct, IFilterAdapter
        {
            var absent = (pattern[index >> 6] >> index) & 1;
            return filter.MayContain(LayoutScenario.Key(start + index + ((long)absent * itemCount))) ? 1 : 0;
        }
    }
}
