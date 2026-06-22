using AssertionException = Meziantou.Framework.Assertions.AssertionException;
using AssertionsAssert = Meziantou.Framework.Assertions.Assert;

namespace Meziantou.Framework.Assertions.Tests;

public sealed class AssertionExceptionTests
{
    [Fact]
    public void True_Success()
    {
        AssertionsAssert.True(true);
        AssertionsAssert.True(true, "custom message");
    }

    [Fact]
    public void True_Fail()
    {
        Validate(() => AssertionsAssert.True(false), """
            Assert.True() assertion failed.
            Expression: false
            Expected: true
            Actual:   false
            """);

        Validate(() => AssertionsAssert.True(false, "custom message"), """
            Assert.True() assertion failed.
            Expression: false
            Expected: true
            Actual:   false
            Message: custom message
            """);
    }

    [Fact]
    public void Equal_EscapesStringValues()
    {
        var expected = "Hello\n\"World\"";
        var actual = "Hello\tWorld";

        Validate(() => AssertionsAssert.Equal(expected, actual), """
            Assert.Equal() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Expected: "Hello\n\"World\""
            Actual:   "Hello\tWorld"
            """);
    }

    [Fact]
    public void Equal_HighlightsCollectionDifference()
    {
        IEnumerable<int> expected = Enumerable.Range(0, 20).ToArray();
        var actual = Enumerable.Range(0, 20).ToArray();
        actual[12] = 42;
        IEnumerable<int> actualEnumerable = actual;

        Validate(() => AssertionsAssert.Equal<int>(expected, actualEnumerable), """
            Assert.Equal() assertion failed: Lengths differ.
            Expected expression: expected
            Actual expression:   actualEnumerable
            Index of first difference: 12
            Expected: [0, 1, 2, ..., 10, 11, 1̲2̲, 13, 14, ...]
            Actual:   [0, 1, 2, ..., 10, 11, 4̲2̲, 13, 14, ...]
            """);
    }

    [Fact]
    public void Equal_HighlightsReadOnlySpanDifference()
    {
        Validate(() => AssertionsAssert.Equal<int>([1, 2, 3], [1, 42, 3]), """
            Assert.Equal() assertion failed: Item at index 1 differs.
            Expected expression: [1, 2, 3]
            Actual expression:   [1, 42, 3]
            Index of first difference: 1
            Expected item: [1, 2̲, 3]
            Actual item:   [1, 4̲2̲, 3]
            """);
    }

    [Fact]
    public void Equal_FormatsCircularCollections()
    {
        object? expected = null;
        var actual = new List<object?>();
        actual.Add(actual);

        Validate(() => AssertionsAssert.Equal(expected, actual), """
            Assert.Equal() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Expected: <null>
            Actual:   [<circular reference>]
            """);
    }

    private static void Validate(Action action, string expectedMessage)
    {
        var exception = global::Xunit.Assert.Throws<AssertionException>(action);
        global::Xunit.Assert.Equal(expectedMessage, exception.Message);
    }
}
