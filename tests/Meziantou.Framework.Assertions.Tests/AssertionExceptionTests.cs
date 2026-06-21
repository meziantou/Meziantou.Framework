using AssertionException = Meziantou.Framework.Assertions.AssertionException;
using Assert = Meziantou.Framework.Assertions.Assert;
using Microsoft.ApplicationInsights.Extensibility.Implementation;


namespace Meziantou.Framework.Assertions.Tests;

public sealed class AssertionExceptionTests
{
    [Fact]
    public void True_Success()
    {
        Assert.True(true);
        Assert.True(true, "custom message");
    }

    [Fact]
    public void True_Fail()
    {
        Validate(() => Assert.True(false), """
            Assert.True() assertion failed.
            Expected: true
            Actual: false
            """);

        Validate(() => Assert.True(false, "custom message"), """
            Assert.True() assertion failed.
            Expected: true
            Actual: false
            Message: custom message
            """);
    }

    private static void Validate(Action action, string? expectedMessage = null)
    {
        try
        {
            action();
            if(expectedMessage is not null)
            {
                Assert.Fail("Expected an exception to be thrown.");
            }
        }
        catch (AssertionException ex)
        {
            if (expectedMessage != null)
            {
                Assert.Equal(expectedMessage, ex.Message);
            }
        }
    }
}
