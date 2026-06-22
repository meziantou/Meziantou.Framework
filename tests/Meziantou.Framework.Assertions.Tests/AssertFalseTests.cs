using AssertionsAssert = Meziantou.Framework.Assertions.Assert;

namespace Meziantou.Framework.Assertions.Tests;

public sealed class AssertFalseTests
{
    [Fact]
    public void Success()
    {
        AssertionsAssert.False(false);
        AssertionsAssert.False(false, "custom message");
    }

    [Fact]
    public void Fails()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.False(true), """
            Assert.False() assertion failed.
            Expression: true
            Expected: false
            Actual:   true
            """);

        AssertionTestHelpers.Validate(() => AssertionsAssert.False(true, "custom message"), """
            Assert.False() assertion failed.
            Expression: true
            Expected: false
            Actual:   true
            Message: custom message
            """);
    }
}
