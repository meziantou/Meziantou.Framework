using BenchmarkDotNet.Attributes;
using Meziantou.Framework.Assertions;

namespace AssertionsBenchmarks;

/// <summary>Measures the success path of the scalar assertions.</summary>
[MemoryDiagnoser]
public class ValueAssertionBenchmark
{
    private const int OperationsPerInvoke = 1000;

    private readonly bool _condition = bool.Parse(bool.TrueString);
    private readonly string _left = "The quick brown fox jumps over the lazy dog";
    private readonly string _right = "The quick brown fox jumps over the lazy dog";
    private readonly object _boxedLeft = 42;
    private readonly object _boxedRight = 42;
    private readonly Uri _uri = new("https://www.meziantou.net/");
    private readonly Uri _sameUri = new("https://www.meziantou.net/");

    [Benchmark(OperationsPerInvoke = OperationsPerInvoke)]
    public void True()
    {
        for (var i = 0; i < OperationsPerInvoke; i++)
        {
            Assert.True(_condition);
        }
    }

    [Benchmark(OperationsPerInvoke = OperationsPerInvoke)]
    public void NotNull()
    {
        for (var i = 0; i < OperationsPerInvoke; i++)
        {
            Assert.NotNull(_left);
        }
    }

    [Benchmark(OperationsPerInvoke = OperationsPerInvoke)]
    public void EqualInt32()
    {
        for (var i = 0; i < OperationsPerInvoke; i++)
        {
            Assert.Equal(42, 42);
        }
    }

    [Benchmark(OperationsPerInvoke = OperationsPerInvoke)]
    public void EqualString()
    {
        for (var i = 0; i < OperationsPerInvoke; i++)
        {
            Assert.Equal(_left, _right);
        }
    }

    [Benchmark(OperationsPerInvoke = OperationsPerInvoke)]
    public void EqualStringAsObject()
    {
        for (var i = 0; i < OperationsPerInvoke; i++)
        {
            Assert.Equal((object)_left, (object)_right);
        }
    }

    [Benchmark(OperationsPerInvoke = OperationsPerInvoke)]
    public void EqualBoxedInt32()
    {
        for (var i = 0; i < OperationsPerInvoke; i++)
        {
            Assert.Equal(_boxedLeft, _boxedRight);
        }
    }

    [Benchmark(OperationsPerInvoke = OperationsPerInvoke)]
    public void EqualReferenceType()
    {
        for (var i = 0; i < OperationsPerInvoke; i++)
        {
            Assert.Equal(_uri, _sameUri);
        }
    }

    [Benchmark(OperationsPerInvoke = OperationsPerInvoke)]
    public void EqualDouble()
    {
        for (var i = 0; i < OperationsPerInvoke; i++)
        {
            Assert.Equal(1.5, 1.5);
        }
    }

    [Benchmark(OperationsPerInvoke = OperationsPerInvoke)]
    public void NotEqualInt32()
    {
        for (var i = 0; i < OperationsPerInvoke; i++)
        {
            Assert.NotEqual(42, 1);
        }
    }

    [Benchmark(OperationsPerInvoke = OperationsPerInvoke)]
    public void EqualStringIgnoreLineEndings()
    {
        for (var i = 0; i < OperationsPerInvoke; i++)
        {
            Assert.Equal(_left, _right, ignoreCase: false, ignoreLineEndingDifferences: true);
        }
    }

    [Benchmark(OperationsPerInvoke = OperationsPerInvoke)]
    public void StartsWith()
    {
        for (var i = 0; i < OperationsPerInvoke; i++)
        {
            Assert.StartsWith("The quick", _right);
        }
    }
}
