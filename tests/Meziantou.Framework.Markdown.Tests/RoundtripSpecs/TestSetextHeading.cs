using static Meziantou.Framework.Markdown.Tests.TestRoundtrip;

namespace Meziantou.Framework.Markdown.Tests.RoundtripSpecs;

public class TestSetextHeading
{
    [Theory]
    [InlineData("h1\n===")] //3
    [InlineData("h1\n ===")] //3
    [InlineData("h1\n  ===")] //3
    [InlineData("h1\n   ===")] //3
    [InlineData("h1\n=== ")] //3
    [InlineData("h1 \n===")] //3
    [InlineData("h1\\\n===")] //3
    [InlineData("h1\n === ")] //3
    [InlineData("h1\nh1 l2\n===")] //3
    [InlineData("h1\n====")] // 4
    [InlineData("h1\n ====")] // 4
    [InlineData("h1\n==== ")] // 4
    [InlineData("h1\n ==== ")] // 4
    [InlineData("h1\n===\nh1\n===")] //3
    [InlineData("\\>h1\n===")] //3
    public void Test(string value)
    {
        RoundTrip(value);
    }

    [Theory]
    [InlineData("h1\r===")]
    [InlineData("h1\n===")]
    [InlineData("h1\r\n===")]

    [InlineData("h1\r===\r")]
    [InlineData("h1\n===\r")]
    [InlineData("h1\r\n===\r")]

    [InlineData("h1\r===\n")]
    [InlineData("h1\n===\n")]
    [InlineData("h1\r\n===\n")]

    [InlineData("h1\r===\r\n")]
    [InlineData("h1\n===\r\n")]
    [InlineData("h1\r\n===\r\n")]

    [InlineData("h1\n===\n\n\nh2---\n\n")]
    [InlineData("h1\r===\r\r\rh2---\r\r")]
    [InlineData("h1\r\n===\r\n\r\n\r\nh2---\r\n\r\n")]
    public void TestNewline(string value)
    {
        RoundTrip(value);
    }
}
