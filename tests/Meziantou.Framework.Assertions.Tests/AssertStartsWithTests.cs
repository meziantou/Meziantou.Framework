using AssertionsAssert = Meziantou.Framework.Assertions.Assert;

namespace Meziantou.Framework.Assertions.Tests;

public sealed class AssertStartsWithTests
{
    [Fact]
    public void Value_Success()
    {
        AssertionsAssert.StartsWith(1, [1, 2, 3]);
        AssertionsAssert.StartsWith("a", ["A", "b"], StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Value_Fails()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.StartsWith(1, [2, 3]), """
            Assert.StartsWith() assertion failed.
            Expected expression: 1
            Actual expression:   [2, 3]
            Expected prefix: 1
            Actual:          [2̲, 3]
            """);
    }

    [Fact]
    public void ValueEnumerable_Success()
    {
        AssertionsAssert.StartsWith(1, Enumerable.Range(1, 3));
        AssertionsAssert.StartsWith("a", new[] { "A", "b" }.AsEnumerable(), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValueEnumerable_Fails()
    {
        IEnumerable<int> actual = [2, 3];

        AssertionTestHelpers.Validate(() => AssertionsAssert.StartsWith(1, actual), """
            Assert.StartsWith() assertion failed.
            Expected expression: 1
            Actual expression:   actual
            Expected prefix: 1
            Actual:          [2̲, 3]
            """);
    }

    [Fact]
    public void ValueNonGenericEnumerable_Success()
    {
        System.Collections.IEnumerable actual = new object[] { 1, 2, 3 };

        AssertionsAssert.StartsWith(1, actual);
    }

    [Fact]
    public void ValueNonGenericEnumerable_Fails()
    {
        System.Collections.IEnumerable actual = new object[] { 2, 3 };

        AssertionTestHelpers.Validate(() => AssertionsAssert.StartsWith(1, actual), """
            Assert.StartsWith() assertion failed.
            Expected expression: 1
            Actual expression:   actual
            Expected prefix: 1
            Actual:          [2̲, 3]
            """);
    }

    [Fact]
    public void Span_Success()
    {
        AssertionsAssert.StartsWith<int>([1, 2], [1, 2, 3]);
        AssertionsAssert.StartsWith<string>(["a", "b"], ["A", "B", "c"], StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Span_FailsWhenItemDiffers()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.StartsWith<int>([1, 2], [1, 42, 3]), """
            Assert.StartsWith() assertion failed.
            Expected expression: [1, 2]
            Actual expression:   [1, 42, 3]
            Index of first difference: 1
            Expected prefix: [1, 2̲]
            Actual:          [1, 4̲2̲, 3]
            """);
    }

    [Fact]
    public void Span_FailsWhenActualIsShorter()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.StartsWith<int>([1, 2, 3], [1, 2]), """
            Assert.StartsWith() assertion failed.
            Expected expression: [1, 2, 3]
            Actual expression:   [1, 2]
            Index of first difference: 2
            Expected prefix: [1, 2, 3̲]
            Actual:          [1, 2]
            """);
    }

    [Fact]
    public void CharSpan_Success()
    {
        AssertionsAssert.StartsWith("Hel".AsSpan(), "Hello".AsSpan());
        AssertionsAssert.StartsWith("hel".AsSpan(), "Hello".AsSpan(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CharSpan_Fails()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.StartsWith("He".AsSpan(), "hello".AsSpan()), """
            Assert.StartsWith() assertion failed.
            Expected expression: "He".AsSpan()
            Actual expression:   "hello".AsSpan()
            Comparison: Ordinal
            Index of first difference: 0
            Expected prefix: "H̲e"
            Actual:          "h̲ello"
            """);
    }

    [Fact]
    public void String_Success()
    {
        AssertionsAssert.StartsWith("Hel", "Hello");
        AssertionsAssert.StartsWith("hel", "Hello", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void String_Fails()
    {
        var expected = "He";
        var actual = "hello";

        AssertionTestHelpers.Validate(() => AssertionsAssert.StartsWith(expected, actual), """
            Assert.StartsWith() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Comparison: Ordinal
            Index of first difference: 0
            Expected prefix: "H̲e"
            Actual:          "h̲ello"
            """);
    }

    [Fact]
    public async Task EnumerableAsyncEnumerable_Success()
    {
        IEnumerable<string> expected = ["a", "b"];
        var actual = AssertionTestHelpers.ToAsyncEnumerable(["A", "B", "c"]);

        await AssertionsAssert.StartsWith(expected, actual, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EnumerableAsyncEnumerable_FailsWhenItemDiffers()
    {
        IEnumerable<int> expected = Enumerable.Range(0, 20).ToArray();
        var actualValues = Enumerable.Range(0, 20).ToArray();
        actualValues[12] = 42;
        var actual = AssertionTestHelpers.ToAsyncEnumerable(actualValues);

        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.StartsWith(expected, actual), """
            Assert.StartsWith() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Index of first difference: 12
            Expected prefix: [0, 1, 2, ..., 10, 11, 1̲2̲, 13, 14, ...]
            Actual:          [0, 1, 2, ..., 10, 11, 4̲2̲, 13, 14, ...]
            """);
    }

    [Fact]
    public void NonGenericEnumerable_Success()
    {
        System.Collections.IEnumerable expected = new object[] { "a", "b" };
        System.Collections.IEnumerable actual = new object[] { "A", "B", "c" };

        AssertionsAssert.StartsWith(expected, actual, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void NonGenericEnumerable_FailsWhenActualIsShorter()
    {
        System.Collections.IEnumerable expected = new object[] { 1, 2, 3 };
        System.Collections.IEnumerable actual = new object[] { 1, 2 };

        AssertionTestHelpers.Validate(() => AssertionsAssert.StartsWith(expected, actual), """
            Assert.StartsWith() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Index of first difference: 2
            Expected prefix: [1, 2, 3̲]
            Actual:          [1, 2]
            """);
    }

    [Fact]
    public void DoesNotStartWith_Success()
    {
        AssertionsAssert.DoesNotStartWith(2, [1, 2, 3]);
        AssertionsAssert.DoesNotStartWith("He", "hello", StringComparison.Ordinal);
    }

    [Fact]
    public void DoesNotStartWith_Fails()
    {
        var expected = 1;
        var actual = new[] { 1, 2, 3 };

        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotStartWith(expected, actual), """
            Assert.DoesNotStartWith() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Not expected prefix: 1
            Actual:              [1, 2, 3]
            """);
    }

    [Fact]
    public async Task DoesNotStartWith_AsyncEnumerableFails()
    {
        IEnumerable<int> expected = [1, 2];
        var actual = AssertionTestHelpers.ToAsyncEnumerable([1, 2, 3]);

        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.DoesNotStartWith(expected, actual), """
            Assert.DoesNotStartWith() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Not expected prefix: [1, 2]
            Actual:              [1, 2, 3]
            """);
    }
}
