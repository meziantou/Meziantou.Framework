using static Meziantou.Framework.Markdown.Tests.TestRoundtrip;

namespace Meziantou.Framework.Markdown.Tests.RoundtripSpecs.Inlines;

public class TestImageInline
{
    [Theory]
    [InlineData("![](a)")]
    [InlineData(" ![](a)")]
    [InlineData("![](a) ")]
    [InlineData(" ![](a) ")]
    [InlineData("   ![description](http://example.com)")]
    public void Test(string value)
    {
        RoundTrip(value);
    }

    [Theory]
    [InlineData("paragraph   ![description](http://example.com)")]
    public void TestParagraph(string value)
    {
        RoundTrip(value);
    }
}
