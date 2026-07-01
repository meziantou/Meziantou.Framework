using AssertionsAssert = Meziantou.Framework.Assertions.Assert;

namespace Meziantou.Framework.Assertions.Tests;

public sealed class AssertEqualUnorderedTests
{
    [Fact]
    public void Enumerable_Success()
    {
        IEnumerable<int> expected = [1, 2];
        IEnumerable<int> actual = [2, 1];

        AssertionsAssert.EqualUnordered(expected, actual);
    }

    [Fact]
    public void EnumerableWithDuplicates_Success()
    {
        IEnumerable<int> expected = [1, 1, 2];
        IEnumerable<int> actual = [2, 1, 1];

        AssertionsAssert.EqualUnordered(expected, actual);
    }

    [Fact]
    public void EnumerableWithDuplicatesAndNulls_Success()
    {
        IEnumerable<string?> expected = ["a", null, "a", "b"];
        IEnumerable<string?> actual = ["b", "a", null, "a"];

        AssertionsAssert.EqualUnordered(expected, actual);
    }

    [Fact]
    public void EnumerableWithDuplicates_Fails()
    {
        IEnumerable<int> expected = [1, 1, 2];
        IEnumerable<int> actual = [1, 2, 2];

        AssertionTestHelpers.Validate(() => AssertionsAssert.EqualUnordered(expected, actual), """
            Assert.EqualUnordered() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Missing expected item index: 1
            Unexpected actual item index: 2
            Expected: [1, 1̲, 2]
            Actual:   [1, 2, 2̲]
            """);
    }

    [Fact]
    public void Enumerable_FailsWhenActualHasMissingItem()
    {
        IEnumerable<int> expected = [1, 2, 3];
        IEnumerable<int> actual = [2, 1];

        AssertionTestHelpers.Validate(() => AssertionsAssert.EqualUnordered(expected, actual), """
            Assert.EqualUnordered() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Missing expected item index: 2
            Expected: [1, 2, 3̲]
            Actual:   [2, 1]
            """);
    }

    [Fact]
    public void Enumerable_FailsWhenActualIsNull()
    {
        IEnumerable<int> expected = [1, 2, 3];
        IEnumerable<int>? actual = null;

        AssertionTestHelpers.Validate(() => AssertionsAssert.EqualUnordered(expected, actual), """
            Assert.EqualUnordered() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Expected: [1, 2, 3]
            Actual:   <null>
            """);
    }

    [Fact]
    public void Enumerable_FailsWhenActualHasUnexpectedItem()
    {
        IEnumerable<int> expected = [1, 2];
        IEnumerable<int> actual = [2, 1, 3];

        AssertionTestHelpers.Validate(() => AssertionsAssert.EqualUnordered(expected, actual), """
            Assert.EqualUnordered() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Unexpected actual item index: 2
            Expected: [1, 2]
            Actual:   [2, 1, 3̲]
            """);
    }

    [Fact]
    public void EnumerableComparer_Success()
    {
        IEnumerable<string> expected = ["a", "b"];
        IEnumerable<string> actual = ["B", "A"];

        AssertionsAssert.EqualUnordered(expected, actual, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnumerableComparer_FailsWithMessage()
    {
        IEnumerable<string> expected = ["a", "b"];
        IEnumerable<string> actual = ["B", "c"];

        AssertionTestHelpers.Validate(() => AssertionsAssert.EqualUnordered(expected, actual, StringComparer.OrdinalIgnoreCase, "custom message"), """
            Assert.EqualUnordered() assertion failed.
            Message: custom message
            Expected expression: expected
            Actual expression:   actual
            Missing expected item index: 0
            Unexpected actual item index: 1
            Expected: ["̲a̲"̲, "b"]
            Actual:   ["B", "̲c̲"̲]
            """);
    }

    [Fact]
    public void DifferentEnumerableTypes_Success()
    {
        IEnumerable<int> expected = [1, 2, 3];
        IEnumerable<long> actual = [3L, 2L, 1L];

        AssertionsAssert.EqualUnordered(expected, actual);
    }

    [Fact]
    public void NonGenericEnumerable_Success()
    {
        System.Collections.IEnumerable expected = new object[] { 1, "a", 3 };
        System.Collections.IEnumerable actual = new object[] { 3L, "a", 1L };

        AssertionsAssert.EqualUnordered(expected, actual);
    }

    [Fact]
    public void NonGenericEnumerable_FailsWhenActualIsNull()
    {
        System.Collections.IEnumerable expected = new object[] { 1, 2, 3 };
        System.Collections.IEnumerable? actual = null;

        AssertionTestHelpers.Validate(() => AssertionsAssert.EqualUnordered(expected, actual), """
            Assert.EqualUnordered() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Expected: [1, 2, 3]
            Actual:   <null>
            """);
    }

    [Fact]
    public async Task AsyncEnumerable_Success()
    {
        var expected = AssertionTestHelpers.ToAsyncEnumerable([1, 2, 3]);
        var actual = AssertionTestHelpers.ToAsyncEnumerable([3, 2, 1]);

        await AssertionsAssert.EqualUnordered(expected, actual);
    }

    [Fact]
    public async Task AsyncEnumerable_Fails()
    {
        var expected = AssertionTestHelpers.ToAsyncEnumerable([1, 1, 2]);
        var actual = AssertionTestHelpers.ToAsyncEnumerable([1, 2, 2]);

        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.EqualUnordered(expected, actual), """
            Assert.EqualUnordered() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Missing expected item index: 1
            Unexpected actual item index: 2
            Expected: [1, 1̲, 2]
            Actual:   [1, 2, 2̲]
            """);
    }

    [Fact]
    public async Task AsyncEnumerable_FailsWhenActualIsNull()
    {
        var expected = AssertionTestHelpers.ToAsyncEnumerable([1, 2, 3]);
        IAsyncEnumerable<int>? actual = null;

        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.EqualUnordered(expected, actual), """
            Assert.EqualUnordered() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Expected: [1, 2, 3]
            Actual:   <null>
            """);
    }

    [Fact]
    public void NotEqualUnordered_Success()
    {
        AssertionsAssert.NotEqualUnordered([1, 2], [1, 3]);

        IEnumerable<int> expected = [1, 2];
        IEnumerable<int>? actual = null;
        System.Collections.IEnumerable expectedNonGeneric = new object[] { 1, 2 };
        System.Collections.IEnumerable? actualNonGeneric = null;

        AssertionsAssert.NotEqualUnordered(expected, actual);
        AssertionsAssert.NotEqualUnordered(expectedNonGeneric, actualNonGeneric);
    }

    [Fact]
    public async Task NotEqualUnordered_AsyncEnumerableSucceedsWhenActualIsNull()
    {
        var expected = AssertionTestHelpers.ToAsyncEnumerable([1, 2]);
        IAsyncEnumerable<int>? actual = null;

        await AssertionsAssert.NotEqualUnordered(expected, actual);
    }

    [Fact]
    public void NotEqualUnordered_Fails()
    {
        var expected = new[] { 1, 2 };
        var actual = new[] { 2, 1 };

        AssertionTestHelpers.Validate(() => AssertionsAssert.NotEqualUnordered(expected, actual), """
            Assert.NotEqualUnordered() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Not expected: [1, 2]
            Actual:       [2, 1]
            """);
    }

    [Fact]
    public async Task NotEqualUnordered_AsyncEnumerableFails()
    {
        var expected = AssertionTestHelpers.ToAsyncEnumerable([1, 2]);
        var actual = AssertionTestHelpers.ToAsyncEnumerable([2, 1]);

        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.NotEqualUnordered(expected, actual), """
            Assert.NotEqualUnordered() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Not expected: [1, 2]
            Actual:       [2, 1]
            """);
    }
}
