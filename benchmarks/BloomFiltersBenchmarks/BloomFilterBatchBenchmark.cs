using BenchmarkDotNet.Attributes;
using Meziantou.Framework.BloomFilters;

namespace BloomFiltersBenchmarks;

[MemoryDiagnoser]
public class BloomFilterBatchBenchmark
{
    private const int ValueCount = 10_000;

    private static readonly BloomFilterSize FilterSize = BloomFilterSize.CreateOptimalSize(ValueCount, 0.01);

    private readonly int[] _intValues = Enumerable.Range(0, ValueCount).ToArray();
    private readonly long[] _longValues = Enumerable.Range(0, ValueCount).Select(value => (long)value).ToArray();
    private BloomFilterXXHash32 _xxHash32 = null!;
    private BloomFilterXXHash64 _xxHash64 = null!;
    private BloomFilterXXHash128 _xxHash128 = null!;

    [IterationSetup]
    public void Setup()
    {
        _xxHash32 = BloomFilter.CreateXXHash32(FilterSize);
        _xxHash64 = BloomFilter.CreateXXHash64(FilterSize);
        _xxHash128 = BloomFilter.CreateXXHash128(FilterSize);
    }

    [Benchmark(Baseline = true)]
    public void XXHash32_Loop()
    {
        foreach (var value in _intValues)
        {
            _xxHash32.Add(value);
        }
    }

    [Benchmark]
    public void XXHash32_AddRange() => _xxHash32.AddRange(_intValues);

    [Benchmark]
    public void XXHash64_Loop()
    {
        foreach (var value in _longValues)
        {
            _xxHash64.Add(value);
        }
    }

    [Benchmark]
    public void XXHash64_AddRange() => _xxHash64.AddRange(_longValues);

    [Benchmark]
    public void XXHash128_Loop()
    {
        foreach (var value in _longValues)
        {
            _xxHash128.Add(value);
        }
    }

    [Benchmark]
    public void XXHash128_AddRange() => _xxHash128.AddRange(_longValues);
}
