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
            Actual:          [1, 2, 1̲]
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
            Actual:          ["a", "̲A̲"̲]
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
            Actual:          "aba̲"
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
            Actual:          "aba̲"
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
            Actual:          [1, 2, 1̲]
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
            Actual:          ["a", "̲A̲"̲]
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
            Actual:          ["a", <null>, <̲n̲u̲l̲l̲>̲]
            """);
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
            Actual:          [1, 2, 1̲]
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
            Actual:          [1, 2, 1̲]
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
}
