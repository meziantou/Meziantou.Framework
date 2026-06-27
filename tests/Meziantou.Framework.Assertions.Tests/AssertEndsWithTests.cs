using AssertionsAssert = Meziantou.Framework.Assertions.Assert;

namespace Meziantou.Framework.Assertions.Tests;

public sealed class AssertEndsWithTests
{
    [Fact]
    public void Value_Success()
    {
        AssertionsAssert.EndsWith(3, new[] { 1, 2, 3 }.AsSpan());
        AssertionsAssert.EndsWith("c", new[] { "A", "B", "C" }.AsSpan(), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Value_Fails()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.EndsWith(4, new[] { 1, 2, 3 }.AsSpan()), """
            Assert.EndsWith() assertion failed.
            Expected expression: 4
            Actual expression:   new[] { 1, 2, 3 }.AsSpan()
            Expected suffix: 4
            Actual:          [1, 2, 3̲]
            """);
    }

    [Fact]
    public void ValueEnumerable_Success()
    {
        AssertionsAssert.EndsWith(3, Enumerable.Range(1, 3));
        AssertionsAssert.EndsWith("c", new[] { "A", "B", "C" }.AsEnumerable(), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValueEnumerable_Fails()
    {
        IEnumerable<int> actual = [1, 2, 3];

        AssertionTestHelpers.Validate(() => AssertionsAssert.EndsWith(4, actual), """
            Assert.EndsWith() assertion failed.
            Expected expression: 4
            Actual expression:   actual
            Expected suffix: 4
            Actual:          [1, 2, 3̲]
            """);
    }

    [Fact]
    public void ValueEnumerable_FailsWhenActualIsNull()
    {
        IEnumerable<int>? actual = null;

        AssertionTestHelpers.Validate(() => AssertionsAssert.EndsWith(4, actual), """
            Assert.EndsWith() assertion failed.
            Expected expression: 4
            Actual expression:   actual
            Expected suffix: 4
            Actual:          <null>
            """);
    }

    [Fact]
    public void ValueNonGenericEnumerable_Success()
    {
        System.Collections.IEnumerable actual = new object[] { 1, 2, 3 };

        AssertionsAssert.EndsWith(3, actual);
    }

    [Fact]
    public void ValueNonGenericEnumerable_Fails()
    {
        System.Collections.IEnumerable actual = new object[] { 1, 2, 3 };

        AssertionTestHelpers.Validate(() => AssertionsAssert.EndsWith(4, actual), """
            Assert.EndsWith() assertion failed.
            Expected expression: 4
            Actual expression:   actual
            Expected suffix: 4
            Actual:          [1, 2, 3̲]
            """);
    }

    [Fact]
    public void ValueNonGenericEnumerable_FailsWhenActualIsNull()
    {
        System.Collections.IEnumerable? actual = null;

        AssertionTestHelpers.Validate(() => AssertionsAssert.EndsWith(4, actual), """
            Assert.EndsWith() assertion failed.
            Expected expression: 4
            Actual expression:   actual
            Expected suffix: 4
            Actual:          <null>
            """);
    }

    [Fact]
    public void Span_Success()
    {
        AssertionsAssert.EndsWith<int>([3, 4], [1, 2, 3, 4]);
        AssertionsAssert.EndsWith<string>(["b", "c"], ["A", "B", "C"], StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Span_FailsWhenItemDiffers()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.EndsWith<int>([2, 4], [1, 2, 3]), """
            Assert.EndsWith() assertion failed.
            Expected expression: [2, 4]
            Actual expression:   [1, 2, 3]
            Index of first difference: 1
            Expected suffix: [2, 4̲]
            Actual:          [1, 2, 3̲]
            """);
    }

    [Fact]
    public void Span_FailsWhenActualIsShorter()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.EndsWith<int>([1, 2, 3], [2, 3]), """
            Assert.EndsWith() assertion failed.
            Expected expression: [1, 2, 3]
            Actual expression:   [2, 3]
            Index of first difference: 2
            Expected suffix: [1, 2, 3̲]
            Actual:          [2, 3̲]
            """);
    }

    [Fact]
    public void String_Success()
    {
        AssertionsAssert.EndsWith("llo", "Hello");
        AssertionsAssert.EndsWith("LLO", "Hello", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void String_Fails()
    {
        var expected = "WORLD";
        var actual = "Hello";

        AssertionTestHelpers.Validate(() => AssertionsAssert.EndsWith(expected, actual), """
            Assert.EndsWith() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Comparison: Ordinal
            Index of first difference: 0
            Expected suffix: "W̲ORLD"
            Actual:          "H̲ello"
            """);
    }

    [Fact]
    public void String_FailsWhenActualIsNull()
    {
        var expected = "WORLD";
        string? actual = null;

        AssertionTestHelpers.Validate(() => AssertionsAssert.EndsWith(expected, actual), """
            Assert.EndsWith() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Comparison: Ordinal
            Expected suffix: "WORLD"
            Actual:          <null>
            """);
    }

    [Fact]
    public async Task EnumerableAsyncEnumerable_Success()
    {
        IEnumerable<string> expected = ["c", "d"];
        var actual = AssertionTestHelpers.ToAsyncEnumerable(["A", "B", "C", "D"]);

        await AssertionsAssert.EndsWith(expected, actual, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EnumerableAsyncEnumerable_FailsWhenItemDiffers()
    {
        IEnumerable<int> expected = [2, 4];
        var actual = AssertionTestHelpers.ToAsyncEnumerable([1, 2, 3]);

        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.EndsWith(expected, actual), """
            Assert.EndsWith() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Index of first difference: 1
            Expected suffix: [2, 4̲]
            Actual:          [1, 2, 3̲]
            """);
    }

    [Fact]
    public async Task EnumerableAsyncEnumerable_FailsWhenActualIsNull()
    {
        IEnumerable<int> expected = [2, 3];
        IAsyncEnumerable<int>? actual = null;

        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.EndsWith(expected, actual), """
            Assert.EndsWith() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Expected suffix: [2, 3]
            Actual:          <null>
            """);
    }

    [Fact]
    public void NonGenericEnumerable_Success()
    {
        System.Collections.IEnumerable expected = new object[] { "c", "d" };
        System.Collections.IEnumerable actual = new object[] { "A", "B", "C", "D" };

        AssertionsAssert.EndsWith(expected, actual, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void NonGenericEnumerable_FailsWhenItemDiffers()
    {
        System.Collections.IEnumerable expected = new object[] { 2, 4 };
        System.Collections.IEnumerable actual = new object[] { 1, 2, 3 };

        AssertionTestHelpers.Validate(() => AssertionsAssert.EndsWith(expected, actual), """
            Assert.EndsWith() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Index of first difference: 1
            Expected suffix: [2, 4̲]
            Actual:          [1, 2, 3̲]
            """);
    }

    [Fact]
    public void NonGenericEnumerable_FailsWhenActualIsNull()
    {
        System.Collections.IEnumerable expected = new object[] { 2, 3 };
        System.Collections.IEnumerable? actual = null;

        AssertionTestHelpers.Validate(() => AssertionsAssert.EndsWith(expected, actual), """
            Assert.EndsWith() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Expected suffix: [2, 3]
            Actual:          <null>
            """);
    }

    [Fact]
    public void DoesNotEndWith_Success()
    {
        AssertionsAssert.DoesNotEndWith(2, [1, 2, 3]);
        AssertionsAssert.DoesNotEndWith("He", "hello", StringComparison.Ordinal);

        IEnumerable<int>? enumerable = null;
        System.Collections.IEnumerable? nonGenericEnumerable = null;
        string? text = null;

        AssertionsAssert.DoesNotEndWith(3, enumerable);
        AssertionsAssert.DoesNotEndWith(3, nonGenericEnumerable);
        AssertionsAssert.DoesNotEndWith("lo", text);
    }

    [Fact]
    public async Task DoesNotEndWith_AsyncEnumerableSucceedsWhenActualIsNull()
    {
        IEnumerable<int> expected = [2, 3];
        IAsyncEnumerable<int>? actual = null;

        await AssertionsAssert.DoesNotEndWith(expected, actual);
    }

    [Fact]
    public void DoesNotEndWith_Fails()
    {
        var expected = 3;
        var actual = new[] { 1, 2, 3 };

        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotEndWith(expected, actual), """
            Assert.DoesNotEndWith() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Not expected suffix: 3
            Actual:              [1, 2, 3]
            """);
    }

    [Fact]
    public async Task DoesNotEndWith_AsyncEnumerableFails()
    {
        IEnumerable<int> expected = [2, 3];
        var actual = AssertionTestHelpers.ToAsyncEnumerable([1, 2, 3]);

        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.DoesNotEndWith(expected, actual), """
            Assert.DoesNotEndWith() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Not expected suffix: [2, 3]
            Actual:              [1, 2, 3]
            """);
    }
}
