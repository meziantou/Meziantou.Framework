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

    [Theory]
    [InlineData("- <a>\n  a\n")]
    [InlineData("- <a>\n    a\n")]
    [InlineData("- <a>\r\n  a\r\n")]
    [InlineData("- <a>\r  a\r")]
    [InlineData("1. <div>\n   x\n   </div>\n")]
    [InlineData("1. <?x\n   y ?>\n")]
    [InlineData("- <!--\n  x\n  -->\n")]
    [InlineData("- a\n\n  <div>\n   x\n")]
    [InlineData("  - <a>\n    a\n")]
    [InlineData("- - <a>\n    a\n")]
    [InlineData("> - <a>\n>   a\n")]
    public void TestInListItem(string value)
    {
        RoundTrip(value);
    }
}
