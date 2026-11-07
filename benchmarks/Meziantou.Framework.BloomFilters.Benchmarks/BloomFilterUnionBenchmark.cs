using BenchmarkDotNet.Attributes;
using Meziantou.Framework.BloomFilters;

namespace Meziantou.Framework.BloomFilters.Benchmarks;

// Merging two filters that each hold half of the values: UnionWith versus adding the values of the source again.
[MemoryDiagnoser]
public class BloomFilterUnionBenchmark
{
    private BloomFilterSize _size;
    private long[] _sourceValues = null!;
    private BloomFilterXXHash128 _source = null!;
    private BloomFilterXXHash128 _destination = null!;

    [Params(1_000_000, 10_000_000)]
    public int ValueCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _size = BloomFilterSize.CreateOptimalSize(ValueCount, 0.01);
        _sourceValues = new long[ValueCount / 2];
        for (var i = 0; i < _sourceValues.Length; i++)
        {
            _sourceValues[i] = i;
        }

        _source = BloomFilter.CreateXXHash128(_size);
        _source.AddRange(_sourceValues);
    }

    // A destination that already holds the source would only measure the words that UnionWith does not need to write,
    // so each iteration starts from a destination that holds only the other half of the values. This limits each
    // iteration to one invocation, which is why the filters are large.
    [IterationSetup]
    public void ResetDestination()
    {
        _destination = BloomFilter.CreateXXHash128(_size);
        for (long i = ValueCount / 2; i < ValueCount; i++)
        {
            _destination.Add(i);
        }
    }

    [Benchmark(Baseline = true)]
    public void ReplayKeys() => _destination.AddRange(_sourceValues);

    [Benchmark]
    public void UnionWith() => _destination.UnionWith(_source);
}
