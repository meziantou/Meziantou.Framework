using AssertionsAssert = Meziantou.Framework.Assertions.Assert;

namespace Meziantou.Framework.Assertions.Tests;

public sealed class AssertDistinctTests
{
    [Fact]
    public void Span_Success()
    {
        AssertionsAssert.Distinct<int>([1, 2, 3]);
    }

    [Fact]
    public void Span_Fails()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.Distinct<int>([1, 2, 1]), """
            Assert.Distinct() assertion failed: Duplicate item found at index 2.
            Expression: [1, 2, 1]
            First index:     0
            Duplicate index: 2
            Actual: [1, 2, 1̲]
            """);
    }

    [Fact]
    public void SpanComparer_Fails()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.Distinct<string>(["a", "A"], StringComparer.OrdinalIgnoreCase), """
            Assert.Distinct() assertion failed: Duplicate item found at index 1.
            Expression: ["a", "A"]
            First index:     0
            Duplicate index: 1
            Actual: ["a", "̲A̲"̲]
            """);
    }

    [Fact]
    public void CharSpan_Fails()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.Distinct("aba".AsSpan()), """
            Assert.Distinct() assertion failed: Duplicate item found at index 2.
            Expression: "aba".AsSpan()
            First index:     0
            Duplicate index: 2
            Actual: "aba̲"
            """);
    }

    [Fact]
    public void String_Fails()
    {
        var actual = "aba";

        AssertionTestHelpers.Validate(() => AssertionsAssert.Distinct(actual), """
            Assert.Distinct() assertion failed: Duplicate item found at index 2.
            Expression: actual
            First index:     0
            Duplicate index: 2
            Actual: "aba̲"
            """);
    }

    [Fact]
    public void Enumerable_Success()
    {
        IEnumerable<int> actual = [1, 2, 3];

        AssertionsAssert.Distinct(actual);
    }

    [Fact]
    public void Enumerable_Fails()
    {
        IEnumerable<int> actual = [1, 2, 1];

        AssertionTestHelpers.Validate(() => AssertionsAssert.Distinct(actual), """
            Assert.Distinct() assertion failed: Duplicate item found at index 2.
            Expression: actual
            First index:     0
            Duplicate index: 2
            Actual: [1, 2, 1̲]
            """);
    }

    [Fact]
    public void EnumerableComparer_Fails()
    {
        IEnumerable<string> actual = ["a", "A"];

        AssertionTestHelpers.Validate(() => AssertionsAssert.Distinct(actual, StringComparer.OrdinalIgnoreCase), """
            Assert.Distinct() assertion failed: Duplicate item found at index 1.
            Expression: actual
            First index:     0
            Duplicate index: 1
            Actual: ["a", "̲A̲"̲]
            """);
    }

    [Fact]
    public void EnumerableNull_Fails()
    {
        IEnumerable<string?> actual = ["a", null, null];

        AssertionTestHelpers.Validate(() => AssertionsAssert.Distinct(actual), """
            Assert.Distinct() assertion failed: Duplicate item found at index 2.
            Expression: actual
            First index:     1
            Duplicate index: 2
            Actual: ["a", <null>, <̲n̲u̲l̲l̲>̲]
            """);
    }

    [Fact]
    public void LargeSpan_Success()
    {
        AssertionsAssert.Distinct<int>(Enumerable.Range(0, 100).ToArray());
    }

    [Fact]
    public void LargeEnumerable_Success()
    {
        AssertionsAssert.Distinct(Enumerable.Range(0, 100));
    }

    [Fact]
    public void LargeEnumerableWithNulls_Success()
    {
        var actual = new string?[100];
        for (var i = 0; i < actual.Length; i++)
        {
            actual[i] = i.ToString(CultureInfo.InvariantCulture);
        }

        actual[50] = null;

        AssertionsAssert.Distinct<string?>(actual);
    }

    [Fact]
    public void LargeEnumerableWithDuplicateBeyondThreshold_Fails()
    {
        // Beyond the linear-search threshold the duplicate is found through the hash lookup.
        var actual = Enumerable.Range(0, 100).ToList();
        actual[99] = 20;

        var exception = AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.Distinct(actual));

        AssertionsAssert.Contains("Duplicate item found at index 99", exception.Message);
        AssertionsAssert.Contains("First index:     20", exception.Message);
    }

    [Fact]
    public void LargeEnumerableWithDuplicateNullBeyondThreshold_Fails()
    {
        var actual = new string?[100];
        for (var i = 0; i < actual.Length; i++)
        {
            actual[i] = i.ToString(CultureInfo.InvariantCulture);
        }

        actual[5] = null;
        actual[99] = null;

        var exception = AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.Distinct<string?>(actual));

        AssertionsAssert.Contains("Duplicate item found at index 99", exception.Message);
        AssertionsAssert.Contains("First index:     5", exception.Message);
    }

    [Fact]
    public void LargeEnumerableComparer_Fails()
    {
        var actual = Enumerable.Range(0, 100).Select(i => i.ToString(CultureInfo.InvariantCulture)).ToList();
        actual[99] = "20";

        var exception = AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.Distinct(actual, StringComparer.OrdinalIgnoreCase));

        AssertionsAssert.Contains("Duplicate item found at index 99", exception.Message);
        AssertionsAssert.Contains("First index:     20", exception.Message);
    }

    [Fact]
    public void NonGenericEnumerable_Fails()
    {
        System.Collections.IEnumerable actual = new object?[] { 1, 2, 1 };

        AssertionTestHelpers.Validate(() => AssertionsAssert.Distinct(actual), """
            Assert.Distinct() assertion failed: Duplicate item found at index 2.
            Expression: actual
            First index:     0
            Duplicate index: 2
            Actual: [1, 2, 1̲]
            """);
    }

    [Fact]
    public async Task AsyncEnumerable_Success()
    {
        var actual = AssertionTestHelpers.ToAsyncEnumerable([1, 2, 3]);

        await AssertionsAssert.Distinct(actual);
    }

    [Fact]
    public async Task AsyncEnumerable_Fails()
    {
        var actual = AssertionTestHelpers.ToAsyncEnumerable([1, 2, 1]);

        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.Distinct(actual), """
            Assert.Distinct() assertion failed: Duplicate item found at index 2.
            Expression: actual
            First index:     0
            Duplicate index: 2
            Actual: [1, 2, 1̲]
            """);
    }

    [Fact]
    public void NotDistinct_Success()
    {
        AssertionsAssert.NotDistinct<int>([1, 2, 1]);
    }

    [Fact]
    public void NotDistinct_Fails()
    {
        var actual = new[] { 1, 2, 3 };

        AssertionTestHelpers.Validate(() => AssertionsAssert.NotDistinct(actual), """
            Assert.NotDistinct() assertion failed.
            Expression: actual
            Not expected: all distinct items
            Actual:       [1, 2, 3]
            """);
    }

    [Fact]
    public void NotDistinct_EnumeratesASingleUseSequenceOnlyOnce()
    {
        var actual = AssertionTestHelpers.SingleUse(1, 2, 3);

        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotDistinct(actual));
    }

    [Fact]
    public void NonGenericEnumerable_LargeCollectionUsesHashLookup()
    {
        var comparer = new CountingEqualityComparer();
        System.Collections.IEnumerable actual = Enumerable.Range(0, 5_000).ToList();

        AssertionsAssert.Distinct(actual, comparer);

        AssertionsAssert.InRange(comparer.EqualsCallCount, 0, 50_000);
    }

    [Fact]
    public void NonGenericEnumerable_DuplicateBeyondThreshold_Fails()
    {
        var items = Enumerable.Range(0, 100).Cast<object?>().ToList();
        items.Add(20);
        System.Collections.IEnumerable actual = items;

        var exception = AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.Distinct(actual));

        AssertionsAssert.Contains("Duplicate item found at index 100", exception.Message);
        AssertionsAssert.Contains("First index:     20", exception.Message);
    }

    [Fact]
    public void NonGenericEnumerable_ValuesOfDifferentTypesBeyondThreshold_AreDistinct()
    {
        // object.Equals(1, 1L) is false, so the hash lookup must not treat them as duplicates either
        var items = Enumerable.Range(0, 100).Cast<object?>().ToList();
        items.Add(20L);
        items.Add(null);
        System.Collections.IEnumerable actual = items;

        AssertionsAssert.Distinct(actual);
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotDistinct(actual));
    }

    [Fact]
    public void NotDistinct_LargeCollectionsUseHashLookup()
    {
        var genericComparer = new CountingEqualityComparer<int>();
        var array = Enumerable.Range(0, 5_000).ToArray();
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotDistinct<int>(array, genericComparer));
        AssertionsAssert.InRange(genericComparer.EqualsCallCount, 0, 50_000);

        genericComparer = new CountingEqualityComparer<int>();
        var list = Enumerable.Range(0, 5_000).ToList();
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotDistinct(list, genericComparer));
        AssertionsAssert.InRange(genericComparer.EqualsCallCount, 0, 50_000);

        var comparer = new CountingEqualityComparer();
        System.Collections.IEnumerable nonGeneric = list;
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotDistinct(nonGeneric, comparer));
        AssertionsAssert.InRange(comparer.EqualsCallCount, 0, 50_000);
    }

    [Fact]
    public async Task NotDistinct_LargeAsyncEnumerableUsesHashLookup()
    {
        var comparer = new CountingEqualityComparer<int>();
        var actual = AssertionTestHelpers.ToAsyncEnumerable(Enumerable.Range(0, 5_000));

        await AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotDistinct(actual, comparer));

        AssertionsAssert.InRange(comparer.EqualsCallCount, 0, 50_000);
    }

    [Fact]
    public async Task NotDistinct_DuplicateBeyondThreshold_Succeeds()
    {
        var values = Enumerable.Range(0, 100).Append(20).ToArray();

        AssertionsAssert.NotDistinct<int>(values);
        AssertionsAssert.NotDistinct(values.ToList());
        AssertionsAssert.NotDistinct((System.Collections.IEnumerable)values.Cast<object?>().ToList());
        AssertionsAssert.NotDistinct(values.Select(i => i.ToString(CultureInfo.InvariantCulture)).Append("A").Append("a").ToList(), StringComparer.OrdinalIgnoreCase);
        await AssertionsAssert.NotDistinct(AssertionTestHelpers.ToAsyncEnumerable(values));
    }

    private sealed class CountingEqualityComparer<T> : IEqualityComparer<T>
    {
        public int EqualsCallCount { get; private set; }

        public bool Equals(T? x, T? y)
        {
            EqualsCallCount++;
            return EqualityComparer<T>.Default.Equals(x, y);
        }

        public int GetHashCode([DisallowNull] T obj) => EqualityComparer<T>.Default.GetHashCode(obj);
    }

    private sealed class CountingEqualityComparer : System.Collections.IEqualityComparer
    {
        public int EqualsCallCount { get; private set; }

        public new bool Equals(object? x, object? y)
        {
            EqualsCallCount++;
            return object.Equals(x, y);
        }

        public int GetHashCode(object obj) => obj.GetHashCode();
    }

}
