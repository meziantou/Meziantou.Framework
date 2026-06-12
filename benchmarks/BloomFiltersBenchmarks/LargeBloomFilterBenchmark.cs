using BenchmarkDotNet.Attributes;
using Meziantou.Framework.BloomFilters;

namespace BloomFiltersBenchmarks;

[MemoryDiagnoser]
public class LargeBloomFilterBenchmark
{
    private const int OperationsPerInvoke = 10_000;

    private static readonly BloomFilterSize FilterSize = BloomFilterSize.CreateExact(128L * 1024 * 1024 * 8, 7);

    private BloomFilterXXHash128 _filter = null!;
    private int[] _existingItems = null!;
    private int[] _newItems = null!;

    [GlobalSetup]
    public void Setup()
    {
        _filter = BloomFilter.CreateXXHash128(FilterSize);
        _existingItems = new int[OperationsPerInvoke];
        _newItems = new int[OperationsPerInvoke];

        for (var i = 0; i < OperationsPerInvoke; i++)
        {
            _existingItems[i] = i;
            _newItems[i] = int.MinValue + i;
            _filter.Add(_existingItems[i]);
        }
    }

    [Benchmark]
    public void Add()
    {
        for (var i = 0; i < OperationsPerInvoke; i++)
        {
            _filter.Add(_newItems[i]);
        }
    }

    [Benchmark]
    public int MayContain_ExistingItems()
    {
        var count = 0;
        for (var i = 0; i < OperationsPerInvoke; i++)
        {
            if (_filter.MayContain(_existingItems[i]))
                count++;
        }

        return count;
    }

    [Benchmark]
    public int MayContain_NewItems()
    {
        var count = 0;
        for (var i = 0; i < OperationsPerInvoke; i++)
        {
            if (_filter.MayContain(_newItems[i]))
                count++;
        }

        return count;
    }
}
