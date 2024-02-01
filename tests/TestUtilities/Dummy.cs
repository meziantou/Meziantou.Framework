using Xunit;
using Xunit.Abstractions;

namespace TestUtilities;

public sealed class Dummy(ITestOutputHelper outputHelper)
{
    [SuppressMessage("Usage", "xUnit1004:Test methods should not be skipped")]
    [Fact(Skip = "For testing purpose")]
    public void Test()
    {
        outputHelper.WriteLine("This test output one line");
        Assert.Fail("This is a dummy test");
    }
}
