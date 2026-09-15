using AssertionsAssert = Meziantou.Framework.Assertions.Assert;

namespace Meziantou.Framework.Assertions.Tests;

public sealed class AssertEmptyTests
{
    [Fact]
    public void Span_Success()
    {
        AssertionsAssert.Empty<int>([]);
    }

    [Fact]
    public void Span_Fails()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.Empty<int>([1, 2, 3]), """
            Assert.Empty() assertion failed.
            Expression: [1, 2, 3]
            Actual: [1̲, 2, 3]
            """);
    }

    [Fact]
    public void CharSpan_Success()
    {
        AssertionsAssert.Empty(ReadOnlySpan<char>.Empty);
    }

    [Fact]
    public void CharSpan_Fails()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.Empty("Hello".AsSpan()), """
            Assert.Empty() assertion failed.
            Expression: "Hello".AsSpan()
            Actual: "H̲ello"
            """);
    }

    [Fact]
    public void String_Success()
    {
        AssertionsAssert.Empty("");
    }

    [Fact]
    public void String_Fails()
    {
        var actual = "Hello";

        AssertionTestHelpers.Validate(() => AssertionsAssert.Empty(actual), """
            Assert.Empty() assertion failed.
            Expression: actual
            Actual: "H̲ello"
            """);
    }

    [Fact]
    public void Enumerable_Success()
    {
        AssertionsAssert.Empty(Enumerable.Empty<int>());
    }

    [Fact]
    public void Enumerable_Fails()
    {
        IEnumerable<int> actual = [1, 2, 3];

        AssertionTestHelpers.Validate(() => AssertionsAssert.Empty(actual), """
            Assert.Empty() assertion failed.
            Expression: actual
            Actual: [1̲, 2, 3]
            """);
    }

    [Fact]
    public void NonGenericEnumerable_Success()
    {
        System.Collections.IEnumerable actual = Array.Empty<object>();

        AssertionsAssert.Empty(actual);
    }

    [Fact]
    public void NonGenericEnumerable_Fails()
    {
        System.Collections.IEnumerable actual = new object[] { 1, 2, 3 };

        AssertionTestHelpers.Validate(() => AssertionsAssert.Empty(actual), """
            Assert.Empty() assertion failed.
            Expression: actual
            Actual: [1̲, 2, 3]
            """);
    }

    [Fact]
    public async Task AsyncEnumerable_Success()
    {
        var actual = AssertionTestHelpers.ToAsyncEnumerable(Array.Empty<int>());

        await AssertionsAssert.Empty(actual);
    }

    [Fact]
    public async Task AsyncEnumerable_Fails()
    {
        var actual = AssertionTestHelpers.ToAsyncEnumerable([1, 2, 3]);

        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.Empty(actual), """
            Assert.Empty() assertion failed.
            Expression: actual
            Actual: [1̲, 2, 3]
            """);
    }

    [Fact]
    public void NotEmpty_Success()
    {
        AssertionsAssert.NotEmpty([1]);
        AssertionsAssert.NotEmpty("a");
    }

    [Fact]
    public void NotEmpty_Fails()
    {
        var actual = Array.Empty<int>();

        AssertionTestHelpers.Validate(() => AssertionsAssert.NotEmpty(actual), """
            Assert.NotEmpty() assertion failed.
            Expression: actual
            Not expected: empty
            Actual:       []
            """);
    }

    [Fact]
    public async Task NullActual_Fails()
    {
        int[]? array = null;
        string? text = null;
        IEnumerable<int>? enumerable = null;
        System.Collections.IEnumerable? nonGeneric = null;
        IAsyncEnumerable<int>? asyncEnumerable = null;

        AssertionTestHelpers.Validate(() => AssertionsAssert.Empty(array), """
            Assert.Empty() assertion failed.
            Expression: array
            Actual: <null>
            """);
        AssertionTestHelpers.Validate(() => AssertionsAssert.Empty(text), """
            Assert.Empty() assertion failed.
            Expression: text
            Actual: <null>
            """);
        AssertionTestHelpers.Validate(() => AssertionsAssert.Empty(enumerable), """
            Assert.Empty() assertion failed.
            Expression: enumerable
            Actual: <null>
            """);
        AssertionTestHelpers.Validate(() => AssertionsAssert.Empty(nonGeneric), """
            Assert.Empty() assertion failed.
            Expression: nonGeneric
            Actual: <null>
            """);
        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.Empty(asyncEnumerable), """
            Assert.Empty() assertion failed.
            Expression: asyncEnumerable
            Actual: <null>
            """);
    }

    [Fact]
    public async Task NotEmpty_NullActual_Fails()
    {
        int[]? array = null;
        string? text = null;
        IEnumerable<int>? enumerable = null;
        System.Collections.IEnumerable? nonGeneric = null;
        IAsyncEnumerable<int>? asyncEnumerable = null;

        AssertionTestHelpers.Validate(() => AssertionsAssert.NotEmpty(array), """
            Assert.NotEmpty() assertion failed.
            Expression: array
            Not expected: empty
            Actual:       <null>
            """);
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotEmpty(text));
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotEmpty(enumerable));
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotEmpty(nonGeneric));
        await AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotEmpty(asyncEnumerable));
    }

    [Fact]
    public void EmptyArray_IsNotNull()
    {
        var actual = Array.Empty<int>();

        AssertionsAssert.Empty(actual);
        AssertionTestHelpers.Validate(() => AssertionsAssert.Empty(new[] { 1 }), """
            Assert.Empty() assertion failed.
            Expression: new[] { 1 }
            Actual: [1̲]
            """);
    }

    [Fact]
    public void NotEmpty_EnumerableSuccess()
    {
        AssertionsAssert.NotEmpty(LazySequence(1));
    }

    [Fact]
    public void NotEmpty_EnumerableFails()
    {
        var actual = LazySequence();

        AssertionTestHelpers.Validate(() => AssertionsAssert.NotEmpty(actual), """
            Assert.NotEmpty() assertion failed.
            Expression: actual
            Not expected: empty
            Actual:       []
            """);
    }

    [Fact]
    public void NotEmpty_NonGenericEnumerableSuccess()
    {
        System.Collections.IEnumerable actual = LazySequence(1);

        AssertionsAssert.NotEmpty(actual);
    }

    [Fact]
    public void NotEmpty_NonGenericEnumerableFails()
    {
        System.Collections.IEnumerable actual = LazySequence();

        AssertionTestHelpers.Validate(() => AssertionsAssert.NotEmpty(actual), """
            Assert.NotEmpty() assertion failed.
            Expression: actual
            Not expected: empty
            Actual:       []
            """);
    }

    [Fact]
    public async Task NotEmpty_AsyncEnumerableSuccess()
    {
        var actual = AssertionTestHelpers.ToAsyncEnumerable([1]);

        await AssertionsAssert.NotEmpty(actual);
    }

    [Fact]
    public async Task NotEmpty_AsyncEnumerableFails()
    {
        var actual = AssertionTestHelpers.ToAsyncEnumerable(Array.Empty<int>());

        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.NotEmpty(actual), """
            Assert.NotEmpty() assertion failed.
            Expression: actual
            Not expected: empty
            Actual:       []
            """);
    }

    [Fact]
    public void CollectionWithKnownCount_IsNotEnumerated()
    {
        AssertionsAssert.Empty(new NonEnumerableCollection(0));
        AssertionsAssert.NotEmpty(new NonEnumerableCollection(2));
        AssertionsAssert.Empty((System.Collections.IEnumerable)new NonEnumerableCollection(0));
        AssertionsAssert.NotEmpty((System.Collections.IEnumerable)new NonEnumerableCollection(2));
    }

    private static IEnumerable<int> LazySequence(params int[] items)
    {
        foreach (var item in items)
        {
            yield return item;
        }
    }

    private sealed class NonEnumerableCollection(int count) : ICollection<int>, System.Collections.ICollection
    {
        public int Count => count;

        public bool IsReadOnly => true;

        public bool IsSynchronized => false;

        public object SyncRoot => this;

        public void Add(int item) => throw new NotSupportedException();

        public void Clear() => throw new NotSupportedException();

        public bool Contains(int item) => throw new NotSupportedException();

        public void CopyTo(int[] array, int arrayIndex) => throw new NotSupportedException();

        public void CopyTo(Array array, int index) => throw new NotSupportedException();

        public bool Remove(int item) => throw new NotSupportedException();

        public IEnumerator<int> GetEnumerator() => throw new InvalidOperationException("The collection must not be enumerated.");

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
