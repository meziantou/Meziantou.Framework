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
            Actual:     []
            """);
    }

    [Fact]
    public void Span_FailsWhenMultiple()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.Single<int>([1, 2, 3]), """
            Assert.Single() assertion failed.
            Expression: [1, 2, 3]
            Actual:     [1, 2̲, 3]
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
            Actual:     "ab̲"
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
            Actual:     "ab̲"
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
            Actual:     []
            """);
    }

    [Fact]
    public void Enumerable_FailsWhenMultiple()
    {
        IEnumerable<int> actual = [1, 2, 3];

        AssertionTestHelpers.Validate(() => AssertionsAssert.Single(actual), """
            Assert.Single() assertion failed.
            Expression: actual
            Actual:     [1, 2̲, 3]
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
            Expression: actual
            Predicate expression: item => item > 3
            Matching items:       []
            """);
    }

    [Fact]
    public void EnumerablePredicate_FailsWhenMultipleMatches()
    {
        IEnumerable<int> actual = [1, 2, 3];

        AssertionTestHelpers.Validate(() => AssertionsAssert.Single(actual, item => item > 1), """
            Assert.Single() assertion failed.
            Expression: actual
            Predicate expression: item => item > 1
            Matching items:       [2, 3̲]
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
            Actual:     [1, 2̲, 3]
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
            Actual:     [1, 2̲, 3]
            """);
    }

    [Fact]
    public void NotSingle_Success()
    {
        AssertionsAssert.NotSingle<int>([]);
        AssertionsAssert.NotSingle<int>([1, 2]);
    }

    [Fact]
    public void NotSingle_Fails()
    {
        var actual = new[] { 42 };

        AssertionTestHelpers.Validate(() => AssertionsAssert.NotSingle(actual), """
            Assert.NotSingle() assertion failed.
            Expression: actual
            Not expected: a single item
            Actual:       [42]
            """);
    }
}
