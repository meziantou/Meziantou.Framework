using AssertionsAssert = Meziantou.Framework.Assertions.Assert;

namespace Meziantou.Framework.Assertions.Tests;

public sealed class AssertContainsTests
{
    [Fact]
    public void Value_Success()
    {
        AssertionsAssert.Contains(2, new[] { 1, 2, 3 }.AsSpan());
        AssertionsAssert.Contains("b", new[] { "A", "B", "C" }.AsSpan(), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Value_Fails()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.Contains(4, new[] { 1, 2, 3 }.AsSpan()), """
            Assert.Contains() assertion failed.
            Expected expression: 4
            Actual expression:   new[] { 1, 2, 3 }.AsSpan()
            Expected item: 4
            Actual:        [1, 2, 3]
            """);
    }

    [Fact]
    public void ValueEnumerable_Success()
    {
        AssertionsAssert.Contains(2, Enumerable.Range(1, 3));
        AssertionsAssert.Contains("b", new[] { "A", "B", "C" }.AsEnumerable(), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValueEnumerable_Fails()
    {
        IEnumerable<int> actual = [1, 2, 3];

        AssertionTestHelpers.Validate(() => AssertionsAssert.Contains(4, actual), """
            Assert.Contains() assertion failed.
            Expected expression: 4
            Actual expression:   actual
            Expected item: 4
            Actual:        [1, 2, 3]
            """);
    }

    [Fact]
    public void Dictionary_Success()
    {
        var actual = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["a"] = 1,
            ["b"] = 2,
        };

        var result = AssertionsAssert.Contains("A", actual);

        global::Xunit.Assert.Equal(1, result);
    }

    [Fact]
    public void ReadOnlyDictionary_Success()
    {
        IReadOnlyDictionary<string, int> actual = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["a"] = 1,
            ["b"] = 2,
        };

        var result = AssertionsAssert.Contains("a", actual);

        global::Xunit.Assert.Equal(1, result);
    }

    [Fact]
    public void GenericDictionary_Success()
    {
        IDictionary<string, int> actual = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["a"] = 1,
            ["b"] = 2,
        };

        var result = AssertionsAssert.Contains("b", actual);

        global::Xunit.Assert.Equal(2, result);
    }

    [Fact]
    public void Dictionary_Fails()
    {
        var actual = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["a"] = 1,
            ["b"] = 2,
        };

        AssertionTestHelpers.Validate(() => AssertionsAssert.Contains("c", actual), """
            Assert.Contains() assertion failed.
            Expected key expression: "c"
            Actual expression:       actual
            Expected key: "c"
            Actual:       ["a": 1, "b": 2]
            """);
    }

    [Fact]
    public void KeyValuePairEnumerableComparer_Success()
    {
        IEnumerable<KeyValuePair<string, int>> actual =
        [
            new("a", 1),
            new("b", 2),
        ];

        var result = AssertionsAssert.Contains("A", actual, StringComparer.OrdinalIgnoreCase);

        global::Xunit.Assert.Equal(1, result);
    }

    [Fact]
    public void KeyValuePairEnumerable_Fails()
    {
        IEnumerable<KeyValuePair<string, int>> actual =
        [
            new("a", 1),
            new("b", 2),
        ];

        AssertionTestHelpers.Validate(() => AssertionsAssert.Contains("c", actual), """
            Assert.Contains() assertion failed.
            Expected key expression: "c"
            Actual expression:       actual
            Expected key: "c"
            Actual:       ["a": 1, "b": 2]
            """);
    }

    [Fact]
    public void ValueNonGenericEnumerable_Success()
    {
        System.Collections.IEnumerable actual = new object[] { 1, 2, 3 };

        AssertionsAssert.Contains(2, actual);
    }

    [Fact]
    public void ValueNonGenericEnumerable_Fails()
    {
        System.Collections.IEnumerable actual = new object[] { 1, 2, 3 };

        AssertionTestHelpers.Validate(() => AssertionsAssert.Contains(4, actual), """
            Assert.Contains() assertion failed.
            Expected expression: 4
            Actual expression:   actual
            Expected item: 4
            Actual:        [1, 2, 3]
            """);
    }

    [Fact]
    public void NonGenericDictionary_Success()
    {
        System.Collections.IDictionary actual = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["a"] = 1,
            ["b"] = 2,
        };

        var result = AssertionsAssert.Contains("b", actual);

        global::Xunit.Assert.Equal(2, result);
    }

    [Fact]
    public void NonGenericDictionary_Fails()
    {
        System.Collections.IDictionary actual = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["a"] = 1,
            ["b"] = 2,
        };

        AssertionTestHelpers.Validate(() => AssertionsAssert.Contains("c", actual), """
            Assert.Contains() assertion failed.
            Expected key expression: "c"
            Actual expression:       actual
            Expected key: "c"
            Actual:       ["a": 1, "b": 2]
            """);
    }

    [Fact]
    public void Span_Success()
    {
        AssertionsAssert.Contains<int>([2, 3], [1, 2, 3, 4]);
        AssertionsAssert.Contains<string>(["b", "c"], ["A", "B", "C"], StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Span_Fails()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.Contains<int>([2, 4], [1, 2, 3]), """
            Assert.Contains() assertion failed.
            Expected expression: [2, 4]
            Actual expression:   [1, 2, 3]
            Expected: [2, 4]
            Actual:   [1, 2, 3]
            """);
    }

    [Fact]
    public void String_Success()
    {
        AssertionsAssert.Contains("ell", "Hello");
        AssertionsAssert.Contains("ELL", "Hello", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void String_Fails()
    {
        var expected = "WORLD";
        var actual = "Hello";

        AssertionTestHelpers.Validate(() => AssertionsAssert.Contains(expected, actual), """
            Assert.Contains() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Comparison: Ordinal
            Expected: "WORLD"
            Actual:   "Hello"
            """);
    }

    [Fact]
    public async Task EnumerableAsyncEnumerable_Success()
    {
        IEnumerable<string> expected = ["b", "c"];
        var actual = AssertionTestHelpers.ToAsyncEnumerable(["A", "B", "C", "D"]);

        await AssertionsAssert.Contains(expected, actual, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EnumerableAsyncEnumerable_Fails()
    {
        IEnumerable<int> expected = [2, 4];
        var actual = AssertionTestHelpers.ToAsyncEnumerable([1, 2, 3]);

        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.Contains(expected, actual), """
            Assert.Contains() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Expected: [2, 4]
            Actual:   [1, 2, 3]
            """);
    }

    [Fact]
    public void NonGenericEnumerable_Success()
    {
        System.Collections.IEnumerable expected = new object[] { "b", "c" };
        System.Collections.IEnumerable actual = new object[] { "A", "B", "C", "D" };

        AssertionsAssert.Contains(expected, actual, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void NonGenericEnumerable_Fails()
    {
        System.Collections.IEnumerable expected = new object[] { 2, 4 };
        System.Collections.IEnumerable actual = new object[] { 1, 2, 3 };

        AssertionTestHelpers.Validate(() => AssertionsAssert.Contains(expected, actual), """
            Assert.Contains() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Expected: [2, 4]
            Actual:   [1, 2, 3]
            """);
    }

    [Fact]
    public void DoesNotContain_Success()
    {
        AssertionsAssert.DoesNotContain(4, [1, 2, 3]);
        AssertionsAssert.DoesNotContain("z", "abc");
    }

    [Fact]
    public void DoesNotContain_Fails()
    {
        var expected = 2;
        var actual = new[] { 1, 2, 3 };

        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotContain(expected, actual), """
            Assert.DoesNotContain() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Not expected item: 2
            Actual:              [1, 2, 3]
            """);
    }

    [Fact]
    public void DoesNotContainDictionary_Fails()
    {
        var actual = new Dictionary<string, int>(StringComparer.Ordinal) { ["a"] = 1 };

        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotContain("a", actual), """
            Assert.DoesNotContain() assertion failed.
            Expected expression: "a"
            Actual expression:   actual
            Not expected key: "a"
            Actual:              [[a, 1]]
            """);
    }
}
