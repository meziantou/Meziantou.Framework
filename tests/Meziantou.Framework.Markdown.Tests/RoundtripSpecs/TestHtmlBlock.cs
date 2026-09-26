using static Meziantou.Framework.Markdown.Tests.TestRoundtrip;

namespace Meziantou.Framework.Markdown.Tests.RoundtripSpecs;

public class TestHtmlBlock
{
    [Theory]
    [InlineData("<br>")]
    [InlineData("<br>\n")]
    [InlineData("<br>\n\n")]
    [InlineData("<div></div>\n\n# h")]
    [InlineData("p\n\n<div></div>\n")]
    public void Test(string value)
    {
        RoundTrip(value);
    }
}
