using AssertionsAssert = Meziantou.Framework.Assertions.Assert;

namespace Meziantou.Framework.Assertions.Tests;

public sealed class AssertNullTests
{
    [Fact]
    public void Success()
    {
        object? actual = null;

        AssertionsAssert.Null(actual);
    }

    [Fact]
    public void Fails()
    {
        var actual = "Hello";

        AssertionTestHelpers.Validate(() => AssertionsAssert.Null(actual), """
            Assert.Null() assertion failed.
            Expression: actual
            Expected: <null>
            Actual:   "Hello"
            """);
    }

    [Fact]
    public void NotNull_Success()
    {
        var actual = "Hello";

        var result = AssertionsAssert.NotNull(actual);

        global::Xunit.Assert.Same(actual, result);
    }

    [Fact]
    public void NotNull_Fails()
    {
        object? actual = null;

        AssertionTestHelpers.Validate(() => AssertionsAssert.NotNull(actual), """
            Assert.NotNull() assertion failed.
            Expression: actual
            Not expected: <null>
            Actual:       <null>
            """);
    }
}
