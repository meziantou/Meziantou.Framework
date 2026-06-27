using AssertionsAssert = Meziantou.Framework.Assertions.Assert;

namespace Meziantou.Framework.Assertions.Tests;

public sealed class AssertFailTests
{
    [Fact]
    public void Fails()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.Fail(), """
            Assert.Fail() assertion failed.
            """);

        AssertionTestHelpers.Validate(() => AssertionsAssert.Fail("custom message"), """
            Assert.Fail() assertion failed.
            Message: custom message
            """);
    }
}
