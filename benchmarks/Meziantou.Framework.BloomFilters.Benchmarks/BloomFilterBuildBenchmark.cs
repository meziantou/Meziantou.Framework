using BenchmarkDotNet.Attributes;
using Meziantou.Framework.BloomFilters;

namespace Meziantou.Framework.BloomFilters.Benchmarks;

/// <summary>
/// Fills a new filter with distinct values up to its design capacity, so every invocation starts from empty bits and
/// the share of positions that are already set grows from 0 to about one half, as when a filter is built for real.
/// </summary>
[MemoryDiagnoser]
public class BloomFilterBuildBenchmark
{
    private const double FalsePositiveProbability = 0.01;

    private long[] _values = null!;
    private BloomFilterSize _size;
    private BlockedBloomFilterSize _blockedSize;

    [Params(100_000, 10_000_000)]
    public int ItemCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _size = BloomFilterSize.CreateOptimalSize(ItemCount, FalsePositiveProbability);
        _blockedSize = BlockedBloomFilterSize.CreateOptimalSize(ItemCount, FalsePositiveProbability);
        _values = new long[ItemCount];
        for (var i = 0; i < _values.Length; i++)
        {
            _values[i] = i;
        }
    }

    [Benchmark]
    public object XXHash64_Add()
    {
        var filter = BloomFilter.CreateXXHash64(_size);
        foreach (var value in _values)
        {
            filter.Add(value);
        }

        return filter;
    }

    [Benchmark]
    public object XXHash64_AddRange()
    {
        var filter = BloomFilter.CreateXXHash64(_size);
        filter.AddRange(_values);
        return filter;
    }

    [Benchmark]
    public object XXHash64_Add_Concurrent()
    {
        var filter = BloomFilter.CreateXXHash64(_size);
        var values = _values;
        var threadCount = Environment.ProcessorCount;
        var chunk = (values.Length + threadCount - 1) / threadCount;
        Parallel.For(0, threadCount, new ParallelOptions { MaxDegreeOfParallelism = threadCount }, thread =>
        {
            var start = thread * chunk;
            var end = Math.Min(values.Length, start + chunk);
            for (var i = start; i < end; i++)
            {
                filter.Add(values[i]);
            }
        });

        return filter;
    }

    [Benchmark]
    public object Blocked_XXHash64_Add()
    {
        var filter = BlockedBloomFilter.CreateXXHash64(_blockedSize);
        foreach (var value in _values)
        {
            filter.Add(value);
        }

        return filter;
    }

    [Benchmark]
    public object Counting_XXHash64_Add()
    {
        var filter = CountingBloomFilter.CreateXXHash64(CountingBloomFilterSize.CreateOptimalSize(ItemCount, FalsePositiveProbability));
        foreach (var value in _values)
        {
            filter.Add(value);
        }

        return filter;
    }
}
