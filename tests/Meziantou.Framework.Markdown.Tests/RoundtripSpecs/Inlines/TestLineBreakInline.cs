using static Meziantou.Framework.Markdown.Tests.TestRoundtrip;

namespace Meziantou.Framework.Markdown.Tests.RoundtripSpecs.Inlines;

public class TestLineBreakInline
{
    [Theory]
    [InlineData("p\n")]
    [InlineData("p\r\n")]
    [InlineData("p\r")]
    [InlineData("[]() ![]()  ``  ` `  `  `  ![]()   ![]()")]
    public void Test(string value)
    {
        RoundTrip(value);
    }
}
