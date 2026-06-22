using AssertionsAssert = Meziantou.Framework.Assertions.Assert;

namespace Meziantou.Framework.Assertions.Tests;

public sealed class AssertEqualTests
{
    [Fact]
    public void EscapesStringValues()
    {
        var expected = "Hello\n\"World\"";
        var actual = "Hello\tWorld";

        AssertionTestHelpers.Validate(() => AssertionsAssert.Equal(expected, actual), """
            Assert.Equal() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Expected: "Hello\n\"World\""
            Actual:   "Hello\tWorld"
            """);
    }

    [Fact]
    public void HighlightsCollectionDifference()
    {
        IEnumerable<int> expected = Enumerable.Range(0, 20).ToArray();
        var actual = Enumerable.Range(0, 20).ToArray();
        actual[12] = 42;
        IEnumerable<int> actualEnumerable = actual;

        AssertionTestHelpers.Validate(() => AssertionsAssert.Equal<int>(expected, actualEnumerable), """
            Assert.Equal() assertion failed: Lengths differ.
            Expected expression: expected
            Actual expression:   actualEnumerable
            Index of first difference: 12
            Expected: [0, 1, 2, ..., 10, 11, 1̲2̲, 13, 14, ...]
            Actual:   [0, 1, 2, ..., 10, 11, 4̲2̲, 13, 14, ...]
            """);
    }

    [Fact]
    public void CollectionComparer_Success()
    {
        IEnumerable<string> expected = ["a", "b"];
        IEnumerable<string> actual = ["A", "B"];

        AssertionsAssert.Equal(expected, actual, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void CollectionComparer_Fails()
    {
        IEnumerable<string> expected = ["a", "b"];
        IEnumerable<string> actual = ["A", "c"];

        AssertionTestHelpers.Validate(() => AssertionsAssert.Equal(expected, actual, StringComparer.OrdinalIgnoreCase, "custom message"), """
            Assert.Equal() assertion failed: Lengths differ.
            Expected expression: expected
            Actual expression:   actual
            Index of first difference: 1
            Expected: ["a", "̲b̲"̲]
            Actual:   ["A", "̲c̲"̲]
            Message: custom message
            """);
    }

    [Fact]
    public async Task HighlightsAsyncCollectionDifference()
    {
        var actual = Enumerable.Range(0, 20).ToArray();
        actual[12] = 42;
        var expectedEnumerable = AssertionTestHelpers.ToAsyncEnumerable(Enumerable.Range(0, 20));
        var actualEnumerable = AssertionTestHelpers.ToAsyncEnumerable(actual);

        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.Equal<int>(expectedEnumerable, actualEnumerable), """
            Assert.Equal() assertion failed: Lengths differ.
            Expected expression: expectedEnumerable
            Actual expression:   actualEnumerable
            Index of first difference: 12
            Expected: [0, 1, 2, ..., 10, 11, 1̲2̲, 13, 14, ...]
            Actual:   [0, 1, 2, ..., 10, 11, 4̲2̲, 13, 14, ...]
            """);
    }

    [Fact]
    public async Task AsyncCollectionComparer_Success()
    {
        var expected = AssertionTestHelpers.ToAsyncEnumerable(["a", "b"]);
        var actual = AssertionTestHelpers.ToAsyncEnumerable(["A", "B"]);

        await AssertionsAssert.Equal(expected, actual, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AsyncCollectionComparer_Fails()
    {
        var expected = AssertionTestHelpers.ToAsyncEnumerable(["a", "b"]);
        var actual = AssertionTestHelpers.ToAsyncEnumerable(["A", "c"]);

        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.Equal(expected, actual, StringComparer.OrdinalIgnoreCase), """
            Assert.Equal() assertion failed: Lengths differ.
            Expected expression: expected
            Actual expression:   actual
            Index of first difference: 1
            Expected: ["a", "̲b̲"̲]
            Actual:   ["A", "̲c̲"̲]
            """);
    }

    [Fact]
    public void NonGenericCollection_Success()
    {
        System.Collections.IEnumerable expected = new object[] { "a", "b" };
        System.Collections.IEnumerable actual = new object[] { "A", "B" };

        AssertionsAssert.Equal(expected, actual, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void NonGenericCollection_Fails()
    {
        System.Collections.IEnumerable expected = new object[] { "a", "b", "c" };
        System.Collections.IEnumerable actual = new object[] { "A", "d", "C" };

        AssertionTestHelpers.Validate(() => AssertionsAssert.Equal(expected, actual, StringComparer.OrdinalIgnoreCase), """
            Assert.Equal() assertion failed: Lengths differ.
            Expected expression: expected
            Actual expression:   actual
            Index of first difference: 1
            Expected: ["a", "̲b̲"̲, "c"]
            Actual:   ["A", "̲d̲"̲, "C"]
            """);
    }

    [Fact]
    public void HighlightsReadOnlySpanDifference()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.Equal<int>([1, 2, 3], [1, 42, 3]), """
            Assert.Equal() assertion failed: Item at index 1 differs.
            Expected expression: [1, 2, 3]
            Actual expression:   [1, 42, 3]
            Index of first difference: 1
            Expected item: [1, 2̲, 3]
            Actual item:   [1, 4̲2̲, 3]
            """);
    }

    [Fact]
    public void FormatsCircularCollections()
    {
        object? expected = null;
        var actual = new List<object?>();
        actual.Add(actual);

        AssertionTestHelpers.Validate(() => AssertionsAssert.Equal(expected, actual), """
            Assert.Equal() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Expected: <null>
            Actual:   [<circular reference>]
            """);
    }
}
