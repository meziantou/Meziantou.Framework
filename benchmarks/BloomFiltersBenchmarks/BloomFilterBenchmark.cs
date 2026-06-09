using BenchmarkDotNet.Attributes;
using Meziantou.Framework.BloomFilters;

namespace BloomFiltersBenchmarks;

[MemoryDiagnoser]
public class BloomFilterBenchmark
{
    private const long ExpectedItemCount = 100_000;
    private const double FalsePositiveProbability = 0.01;

    private static readonly BloomFilterSize FilterSize = BloomFilterSize.CreateOptimalSize(ExpectedItemCount, FalsePositiveProbability);

    private BloomFilterXXHash128 _filter = null!;

    private string[] _items = null!;
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

        _existingItem = _items[ExpectedItemCount / 2];
        _newItem = $"nonexistent_item_{Guid.NewGuid()}";
    }

    [Benchmark]
    public void Add_Int()
    {
        for (var i = 0; i < 1000; i++)
        {
            _filter.Add(i);
        }
    }

    [Benchmark]
    public void Add_String()
    {
        for (var i = 0; i < 1000; i++)
        {
            _filter.Add($"value_{i}");
        }
    }

    [Benchmark]
    public void Add_ReadOnlySpanByte()
    {
        var bytes = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        for (var i = 0; i < 1000; i++)
        {
            _filter.Add(bytes.AsSpan());
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
    public void MayContain_ExistingItems()
    {
        var count = 0;
        for (var i = 0; i < 1000; i++)
        {
            if (_filter.MayContain(_items[i]))
                count++;
        }
    }

    [Benchmark]
    public void MayContain_NewItems()
    {
        var count = 0;
        for (var i = 0; i < 1000; i++)
        {
            if (_filter.MayContain($"nonexistent_{i}"))
                count++;
        }
    }
}
