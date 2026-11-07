using BenchmarkDotNet.Attributes;
using Meziantou.Framework.BloomFilters;

namespace Meziantou.Framework.BloomFilters.Benchmarks;

[MemoryDiagnoser]
public class CountingBloomFilterBenchmark
{
    private const long ExpectedItemCount = 100_000;
    private const int OperationsPerInvoke = 1000;

    private CountingBloomFilterXXHash128 _filter = null!;

    // Empty: no value added. Sparse: 1% of the expected values. Full: the expected number of values.
    [Params("Empty", "Sparse", "Full")]
    public string Fill { get; set; } = "Full";

    [GlobalSetup]
    public void Setup()
    {
        _filter = CountingBloomFilter.CreateXXHash128(CountingBloomFilterSize.CreateOptimalSize(ExpectedItemCount, 0.01));

        var itemCount = Fill switch
        {
            "Empty" => 0,
            "Sparse" => ExpectedItemCount / 100,
            _ => ExpectedItemCount,
        };

        for (var i = 0L; i < itemCount; i++)
        {
            _filter.Add(i);
        }
    }

    [Benchmark(OperationsPerInvoke = OperationsPerInvoke)]
    public int GetEstimatedCount_PresentValues()
    {
        var count = 0;
        for (var i = 0L; i < OperationsPerInvoke; i++)
        {
            count += _filter.GetEstimatedCount(i);
        }

        return count;
    }

    [Benchmark(OperationsPerInvoke = OperationsPerInvoke)]
    public int GetEstimatedCount_AbsentValues()
    {
        var count = 0;
        for (var i = 0L; i < OperationsPerInvoke; i++)
        {
            count += _filter.GetEstimatedCount(-1 - i);
        }

        return count;
    }
}
