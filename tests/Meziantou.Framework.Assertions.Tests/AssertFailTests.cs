using AssertionsAssert = Meziantou.Framework.Assertions.Assert;
using XunitAssert = Xunit.Assert;

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

    [Fact]
    public void XunitSkip_UsesTheFormatExpectedByXunit()
    {
        var exception = AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.XunitSkip("custom reason"));

        AssertionsAssert.Equal("$XunitDynamicSkip$custom reason", exception.Message);
        AssertionsAssert.Equal(GetXunitSkipMessage("custom reason"), exception.Message);
    }

    // xunit only exposes the skip contract through the exception thrown by Assert.Skip
    private static string GetXunitSkipMessage(string reason)
    {
        try
        {
            XunitAssert.Skip(reason);
        }
        catch (Exception ex)
        {
            return ex.Message;
        }

        throw new InvalidOperationException("Xunit.Assert.Skip did not throw");
    }
}
