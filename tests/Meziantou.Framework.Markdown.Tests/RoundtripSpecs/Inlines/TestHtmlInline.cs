using static Meziantou.Framework.Markdown.Tests.TestRoundtrip;

namespace Meziantou.Framework.Markdown.Tests.RoundtripSpecs.Inlines;

public class TestHtmlInline
{
    [Theory]
    [InlineData("<em>f</em>")]
    [InlineData("<em> f</em>")]
    [InlineData("<em>f </em>")]
    [InlineData("<em> f </em>")]
    [InlineData("<b>p</b>")]
    [InlineData("<b></b>")]
    [InlineData("<b> </b>")]
    [InlineData("<b>  </b>")]
    [InlineData("<b>   </b>")]
    [InlineData("<b>\t</b>")]
    [InlineData("<b> \t</b>")]
    [InlineData("<b>\t </b>")]
    [InlineData("<b> \t </b>")]
    public void Test(string value)
    {
        RoundTrip(value);
    }

    [Theory]
    [InlineData("> <a\n> href=\"x\">\n")]
    [InlineData("> x <!--\n> c -->\n")]
    public void TestSpanningLinesInContainer(string value)
    {
        RoundTrip(value);
    }
}
