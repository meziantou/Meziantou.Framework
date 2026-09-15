using AssertionsAssert = Meziantou.Framework.Assertions.Assert;

namespace Meziantou.Framework.Assertions.Tests;

public sealed class AssertSingleTests
{
    [Fact]
    public void Span_Success()
    {
        var result = AssertionsAssert.Single<int>([42]);

        AssertionsAssert.Equal(42, result);
    }

    [Fact]
    public void Span_FailsWhenEmpty()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.Single<int>([]), """
            Assert.Single() assertion failed.
            Expression: []
            Actual: []
            """);
    }

    [Fact]
    public void Span_FailsWhenMultiple()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.Single<int>([1, 2, 3]), """
            Assert.Single() assertion failed.
            Expression: [1, 2, 3]
            Actual: [1, 2̲, 3]
            """);
    }

    [Fact]
    public void CharSpan_Success()
    {
        var result = AssertionsAssert.Single("a".AsSpan());

        AssertionsAssert.Equal('a', result);
    }

    [Fact]
    public void CharSpan_Fails()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.Single("ab".AsSpan()), """
            Assert.Single() assertion failed.
            Expression: "ab".AsSpan()
            Actual: "ab̲"
            """);
    }

    [Fact]
    public void String_Success()
    {
        var result = AssertionsAssert.Single("a");

        AssertionsAssert.Equal('a', result);
    }

    [Fact]
    public void String_Fails()
    {
        var actual = "ab";

        AssertionTestHelpers.Validate(() => AssertionsAssert.Single(actual), """
            Assert.Single() assertion failed.
            Expression: actual
            Actual: "ab̲"
            """);
    }

    [Fact]
    public void Enumerable_Success()
    {
        var result = AssertionsAssert.Single(new[] { 42 }.AsEnumerable());

        AssertionsAssert.Equal(42, result);
    }

    [Fact]
    public void Enumerable_FailsWhenEmpty()
    {
        IEnumerable<int> actual = [];

        AssertionTestHelpers.Validate(() => AssertionsAssert.Single(actual), """
            Assert.Single() assertion failed.
            Expression: actual
            Actual: []
            """);
    }

    [Fact]
    public void Enumerable_FailsWhenMultiple()
    {
        IEnumerable<int> actual = [1, 2, 3];

        AssertionTestHelpers.Validate(() => AssertionsAssert.Single(actual), """
            Assert.Single() assertion failed.
            Expression: actual
            Actual: [1, 2̲, 3]
            """);
    }

    [Fact]
    public void EnumerablePredicate_Success()
    {
        IEnumerable<int> actual = [1, 2, 3];

        var result = AssertionsAssert.Single(actual, item => item % 2 == 0);

        AssertionsAssert.Equal(2, result);
    }

    [Fact]
    public void EnumerablePredicate_FailsWhenNoMatch()
    {
        IEnumerable<int> actual = [1, 2, 3];

        AssertionTestHelpers.Validate(() => AssertionsAssert.Single(actual, item => item > 3), """
            Assert.Single() assertion failed.
            Expression:           actual
            Predicate expression: item => item > 3
            Matching items: []
            """);
    }

    [Fact]
    public void EnumerablePredicate_FailsWhenMultipleMatches()
    {
        IEnumerable<int> actual = [1, 2, 3];

        AssertionTestHelpers.Validate(() => AssertionsAssert.Single(actual, item => item > 1), """
            Assert.Single() assertion failed.
            Expression:           actual
            Predicate expression: item => item > 1
            Matching items: [2, 3̲]
            """);
    }

    [Fact]
    public void NonGenericEnumerable_Success()
    {
        System.Collections.IEnumerable actual = new object[] { 42 };

        var result = AssertionsAssert.Single(actual);

        AssertionsAssert.Equal(42, result);
    }

    [Fact]
    public void NonGenericEnumerable_Fails()
    {
        System.Collections.IEnumerable actual = new object[] { 1, 2, 3 };

        AssertionTestHelpers.Validate(() => AssertionsAssert.Single(actual), """
            Assert.Single() assertion failed.
            Expression: actual
            Actual: [1, 2̲, 3]
            """);
    }

    [Fact]
    public async Task AsyncEnumerable_Success()
    {
        var actual = AssertionTestHelpers.ToAsyncEnumerable([42]);

        var result = await AssertionsAssert.Single(actual);

        AssertionsAssert.Equal(42, result);
    }

    [Fact]
    public async Task AsyncEnumerable_Fails()
    {
        var actual = AssertionTestHelpers.ToAsyncEnumerable([1, 2, 3]);

        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.Single(actual), """
            Assert.Single() assertion failed.
            Expression: actual
            Actual: [1, 2̲, 3]
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

        AssertionTestHelpers.Validate(() => AssertionsAssert.Single(array), """
            Assert.Single() assertion failed.
            Expression: array
            Actual: <null>
            """);
        AssertionTestHelpers.Validate(() => AssertionsAssert.Single(enumerable, item => item > 0), """
            Assert.Single() assertion failed.
            Expression:           enumerable
            Predicate expression: item => item > 0
            Actual: <null>
            """);
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.Single(text));
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.Single(enumerable));
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.Single(nonGeneric));
        await AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.Single(asyncEnumerable));
    }

    [Fact]
    public void Array_Success()
    {
        var actual = new[] { 42 };

        AssertionsAssert.Equal(42, AssertionsAssert.Single(actual));
        AssertionTestHelpers.Validate(() => AssertionsAssert.Single(new[] { 1, 2 }), """
            Assert.Single() assertion failed.
            Expression: new[] { 1, 2 }
            Actual: [1, 2̲]
            """);
    }

    [Fact]
    public void ListWithSingleItem_IsNotEnumerated()
    {
        AssertionsAssert.Equal(42, AssertionsAssert.Single(new NonEnumerableList(42)));
        AssertionsAssert.Equal(42, AssertionsAssert.Single((System.Collections.IEnumerable)new NonEnumerableList(42)));
    }

    private sealed class NonEnumerableList(int item) : IList<int>, System.Collections.IList
    {
        public int Count => 1;

        public bool IsReadOnly => true;

        public bool IsFixedSize => true;

        public bool IsSynchronized => false;

        public object SyncRoot => this;

        public int this[int index]
        {
            get => index is 0 ? item : throw new ArgumentOutOfRangeException(nameof(index));
            set => throw new NotSupportedException();
        }

        object? System.Collections.IList.this[int index]
        {
            get => this[index];
            set => throw new NotSupportedException();
        }

        public int IndexOf(int value) => throw new NotSupportedException();

        public void Insert(int index, int value) => throw new NotSupportedException();

        public void RemoveAt(int index) => throw new NotSupportedException();

        public void Add(int value) => throw new NotSupportedException();

        public void Clear() => throw new NotSupportedException();

        public bool Contains(int value) => throw new NotSupportedException();

        public void CopyTo(int[] array, int arrayIndex) => throw new NotSupportedException();

        public bool Remove(int value) => throw new NotSupportedException();

        int System.Collections.IList.Add(object? value) => throw new NotSupportedException();

        bool System.Collections.IList.Contains(object? value) => throw new NotSupportedException();

        int System.Collections.IList.IndexOf(object? value) => throw new NotSupportedException();

        void System.Collections.IList.Insert(int index, object? value) => throw new NotSupportedException();

        void System.Collections.IList.Remove(object? value) => throw new NotSupportedException();

        void System.Collections.ICollection.CopyTo(Array array, int index) => throw new NotSupportedException();

        public IEnumerator<int> GetEnumerator() => throw new InvalidOperationException("The list must not be enumerated.");

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
