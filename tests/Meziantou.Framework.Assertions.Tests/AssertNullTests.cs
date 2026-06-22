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
}
