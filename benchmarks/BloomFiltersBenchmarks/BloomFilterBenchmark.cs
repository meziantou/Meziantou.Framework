using BenchmarkDotNet.Attributes;
using Meziantou.Framework.BloomFilters;

namespace BloomFiltersBenchmarks;

[MemoryDiagnoser]
public class BloomFilterBenchmark
{
    private const long ExpectedItemCount = 100_000;
    private const double FalsePositiveProbability = 0.01;
    private const int OperationsPerInvoke = 1000;

    private static readonly BloomFilterSize FilterSize = BloomFilterSize.CreateOptimalSize(ExpectedItemCount, FalsePositiveProbability);

    private BloomFilterXXHash128 _filter = null!;

    private byte[] _bytes = null!;
    private string[] _items = null!;
    private string[] _newItems = null!;
    private string _existingItem = null!;
    private string _newItem = null!;

    [GlobalSetup]
    public void Setup()
    {
        _filter = BloomFilter.CreateXXHash128(FilterSize);

        _items = new string[ExpectedItemCount];
        for (var i = 0; i < ExpectedItemCount; i++)
        {
            _items[i] = $"item_{i}";
            _filter.Add(_items[i]);
        }

        _bytes = [1, 2, 3, 4, 5, 6, 7, 8];
        _newItems = new string[OperationsPerInvoke];
        for (var i = 0; i < _newItems.Length; i++)
        {
            _newItems[i] = $"nonexistent_{i}";
        }

        _existingItem = _items[ExpectedItemCount / 2];
        _newItem = $"nonexistent_item_{Guid.NewGuid()}";
    }

    [Benchmark]
    public void Add_Int()
    {
        for (var i = 0; i < OperationsPerInvoke; i++)
        {
            _filter.Add(i);
        }
    }

    [Benchmark]
    public void Add_String()
    {
        for (var i = 0; i < OperationsPerInvoke; i++)
        {
            _filter.Add(_newItems[i]);
        }
    }

    [Benchmark]
    public void Add_ReadOnlySpanByte()
    {
        for (var i = 0; i < OperationsPerInvoke; i++)
        {
            _filter.Add(_bytes.AsSpan());
        }
    }

    [Benchmark]
    public bool MayContain_ExistingItem()
    {
        return _filter.MayContain(_existingItem);
    }

    [Benchmark]
    public bool MayContain_NewItem()
    {
        return _filter.MayContain(_newItem);
    }

    [Benchmark]
    public int MayContain_ExistingItems()
    {
        var count = 0;
        for (var i = 0; i < OperationsPerInvoke; i++)
        {
            if (_filter.MayContain(_items[i]))
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
