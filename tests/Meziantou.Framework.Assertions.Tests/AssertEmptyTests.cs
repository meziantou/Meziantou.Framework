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
            Actual:     [1̲, 2, 3]
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
            Actual:     "H̲ello"
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
            Actual:     "H̲ello"
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
            Actual:     [1̲, 2, 3]
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
            Actual:     [1̲, 2, 3]
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
            Actual:     [1̲, 2, 3]
            """);
    }
}
