using BenchmarkDotNet.Attributes;
using Meziantou.Framework.Assertions;

namespace AssertionsBenchmarks;

/// <summary>Measures the failure path, which builds the assertion message.</summary>
[MemoryDiagnoser]
public class FailureBenchmark
{
    private readonly bool _condition = bool.Parse(bool.FalseString);
    private int[] _expectedArray = null!;
    private int[] _actualArray = null!;

    [GlobalSetup]
    public void Setup()
    {
        _expectedArray = Enumerable.Range(0, 100).ToArray();
        _actualArray = Enumerable.Range(0, 100).ToArray();
        _actualArray[50] = -1;
    }

    [Benchmark]
    public string FalseCondition()
    {
        try
        {
            Assert.True(_condition);
        }
        catch (AssertionException ex)
        {
            return ex.Message;
        }

        return null!;
    }

    [Benchmark]
    public string NotEqualStrings()
    {
        try
        {
            Assert.Equal("The quick brown fox", "The quick brown cat");
        }
        catch (AssertionException ex)
        {
            return ex.Message;
        }

        return null!;
    }

    [Benchmark]
    public string NotEqualCollections()
    {
        try
        {
            Assert.Equal(_expectedArray, _actualArray);
        }
        catch (AssertionException ex)
        {
            return ex.Message;
        }

        return null!;
    }
}
