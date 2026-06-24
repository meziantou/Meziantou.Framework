using AssertionsAssert = Meziantou.Framework.Assertions.Assert;

namespace Meziantou.Framework.Assertions.Tests;

public sealed class AssertSameTests
{
    [Fact]
    public void Success()
    {
        var expected = new object();
        var actual = expected;

        AssertionsAssert.Same(expected, actual);
    }

    [Fact]
    public void SuccessWhenBothNull()
    {
        object? expected = null;
        object? actual = null;

        AssertionsAssert.Same(expected, actual);
    }

    [Fact]
    public void FailsWhenValuesAreEqualButReferencesDiffer()
    {
        var expected = new[] { 1 };
        var actual = new[] { 1 };

        AssertionTestHelpers.Validate(() => AssertionsAssert.Same(expected, actual), """
            Assert.Same() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Expected: same instance as [1]
            Actual:   [1]
            """);
    }

    [Fact]
    public void FailsWhenActualIsNull()
    {
        var expected = new object();
        object? actual = null;

        AssertionTestHelpers.Validate(() => AssertionsAssert.Same(expected, actual), """
            Assert.Same() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Expected: same instance as System.Object
            Actual:   <null>
            """);
    }

    [Fact]
    public void NotSame_Success()
    {
        AssertionsAssert.NotSame(new object(), new object());
    }

    [Fact]
    public void NotSame_Fails()
    {
        var expected = new object();
        var actual = expected;

        AssertionTestHelpers.Validate(() => AssertionsAssert.NotSame(expected, actual), """
            Assert.NotSame() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Not expected: same instance as System.Object
            Actual:       System.Object
            """);
    }
}
