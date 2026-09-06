using BenchmarkDotNet.Attributes;
using Meziantou.Framework.Assertions;

namespace AssertionsBenchmarks;

/// <summary>Measures the success path of the collection assertions.</summary>
[MemoryDiagnoser]
public class CollectionAssertionBenchmark
{
    [Params(8, 1000)]
    public int Size { get; set; }

    private int[] _expectedArray = null!;
    private int[] _actualArray = null!;
    private List<int> _expectedList = null!;
    private List<int> _actualList = null!;
    private string[] _expectedStrings = null!;
    private string[] _actualStrings = null!;
    private IEnumerable<int> _expectedLazy = null!;
    private IEnumerable<int> _actualLazy = null!;
    private int[] _singleItem = null!;
    private int[] _empty = null!;

    [GlobalSetup]
    public void Setup()
    {
        _expectedArray = Enumerable.Range(0, Size).ToArray();
        _actualArray = Enumerable.Range(0, Size).ToArray();
        _expectedList = [.. _expectedArray];
        _actualList = [.. _actualArray];
        _expectedStrings = [.. _expectedArray.Select(value => value.ToString(CultureInfo.InvariantCulture))];
        _actualStrings = [.. _actualArray.Select(value => value.ToString(CultureInfo.InvariantCulture))];
        _expectedLazy = Lazy(_expectedArray);
        _actualLazy = Lazy(_actualArray);
        _singleItem = [1];
        _empty = [];

        static IEnumerable<int> Lazy(int[] values)
        {
            foreach (var value in values)
            {
                yield return value;
            }
        }
    }

    [Benchmark]
    public void EqualArrays() => Assert.Equal(_expectedArray, _actualArray);

    [Benchmark]
    public void EqualLists() => Assert.Equal(_expectedList, _actualList);

    [Benchmark]
    public void EqualStringArrays() => Assert.Equal(_expectedStrings, _actualStrings);

    [Benchmark]
    public void EqualLazyEnumerables() => Assert.Equal(_expectedLazy, _actualLazy);

    [Benchmark]
    public void EqualSpans() => Assert.Equal<int>(_expectedArray.AsSpan(), _actualArray.AsSpan());

    [Benchmark]
    public void EqualNonGeneric() => Assert.Equal((System.Collections.IEnumerable)_expectedArray, (System.Collections.IEnumerable)_actualArray);

    [Benchmark]
    public void EqualUnordered() => Assert.EqualUnordered(_expectedArray, _actualArray);

    [Benchmark]
    public void ContainsLast() => Assert.Contains(Size - 1, (IEnumerable<int>)_actualArray);

    [Benchmark]
    public void Distinct() => Assert.Distinct((IEnumerable<int>)_actualArray);

    [Benchmark]
    public void AllPredicate() => Assert.All(_actualArray, value => value >= 0);

    [Benchmark]
    public void HasCount() => Assert.HasCount(Size, (IEnumerable<int>)_actualArray);

    [Benchmark]
    public void SingleItem() => Assert.Single((IEnumerable<int>)_singleItem);

    [Benchmark]
    public void Empty() => Assert.Empty((IEnumerable<int>)_empty);

    [Benchmark]
    public void NotEmpty() => Assert.NotEmpty((IEnumerable<int>)_actualArray);
}
