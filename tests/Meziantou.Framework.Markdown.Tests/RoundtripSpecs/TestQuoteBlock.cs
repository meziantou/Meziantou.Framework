using static Meziantou.Framework.Markdown.Tests.TestRoundtrip;

namespace Meziantou.Framework.Markdown.Tests.RoundtripSpecs;

public class TestQuoteBlock
{
    [Theory]
    [InlineData(">q")]
    [InlineData(" >q")]
    [InlineData("  >q")]
    [InlineData("   >q")]
    [InlineData("> q")]
    [InlineData(" > q")]
    [InlineData("  > q")]
    [InlineData("   > q")]
    [InlineData(">  q")]
    [InlineData(" >  q")]
    [InlineData("  >  q")]
    [InlineData("   >  q")]

    [InlineData(">q\n>q")]
    [InlineData(">q\n >q")]
    [InlineData(">q\n  >q")]
    [InlineData(">q\n   >q")]
    [InlineData(">q\n> q")]
    [InlineData(">q\n > q")]
    [InlineData(">q\n  > q")]
    [InlineData(">q\n   > q")]
    [InlineData(">q\n>  q")]
    [InlineData(">q\n >  q")]
    [InlineData(">q\n  >  q")]
    [InlineData(">q\n   >  q")]

    [InlineData(" >q\n>q")]
    [InlineData(" >q\n >q")]
    [InlineData(" >q\n  >q")]
    [InlineData(" >q\n   >q")]
    [InlineData(" >q\n> q")]
    [InlineData(" >q\n > q")]
    [InlineData(" >q\n  > q")]
    [InlineData(" >q\n   > q")]
    [InlineData(" >q\n>  q")]
    [InlineData(" >q\n >  q")]
    [InlineData(" >q\n  >  q")]
    [InlineData(" >q\n   >  q")]

    [InlineData("  >q\n>q")]
    [InlineData("  >q\n >q")]
    [InlineData("  >q\n  >q")]
    [InlineData("  >q\n   >q")]
    [InlineData("  >q\n> q")]
    [InlineData("  >q\n > q")]
    [InlineData("  >q\n  > q")]
    [InlineData("  >q\n   > q")]
    [InlineData("  >q\n>  q")]
    [InlineData("  >q\n >  q")]
    [InlineData("  >q\n  >  q")]
    [InlineData("  >q\n   >  q")]

    [InlineData("> q\n>q")]
    [InlineData("> q\n >q")]
    [InlineData("> q\n  >q")]
    [InlineData("> q\n   >q")]
    [InlineData("> q\n> q")]
    [InlineData("> q\n > q")]
    [InlineData("> q\n  > q")]
    [InlineData("> q\n   > q")]
    [InlineData("> q\n>  q")]
    [InlineData("> q\n >  q")]
    [InlineData("> q\n  >  q")]
    [InlineData("> q\n   >  q")]

    [InlineData(" > q\n>q")]
    [InlineData(" > q\n >q")]
    [InlineData(" > q\n  >q")]
    [InlineData(" > q\n   >q")]
    [InlineData(" > q\n> q")]
    [InlineData(" > q\n > q")]
    [InlineData(" > q\n  > q")]
    [InlineData(" > q\n   > q")]
    [InlineData(" > q\n>  q")]
    [InlineData(" > q\n >  q")]
    [InlineData(" > q\n  >  q")]
    [InlineData(" > q\n   >  q")]

    [InlineData("  > q\n>q")]
    [InlineData("  > q\n >q")]
    [InlineData("  > q\n  >q")]
    [InlineData("  > q\n   >q")]
    [InlineData("  > q\n> q")]
    [InlineData("  > q\n > q")]
    [InlineData("  > q\n  > q")]
    [InlineData("  > q\n   > q")]
    [InlineData("  > q\n>  q")]
    [InlineData("  > q\n >  q")]
    [InlineData("  > q\n  >  q")]
    [InlineData("  > q\n   >  q")]

    [InlineData("   > q\n>q")]
    [InlineData("   > q\n >q")]
    [InlineData("   > q\n  >q")]
    [InlineData("   > q\n   >q")]
    [InlineData("   > q\n> q")]
    [InlineData("   > q\n > q")]
    [InlineData("   > q\n  > q")]
    [InlineData("   > q\n   > q")]
    [InlineData("   > q\n>  q")]
    [InlineData("   > q\n >  q")]
    [InlineData("   > q\n  >  q")]
    [InlineData("   > q\n   >  q")]

    [InlineData(">  q\n>q")]
    [InlineData(">  q\n >q")]
    [InlineData(">  q\n  >q")]
    [InlineData(">  q\n   >q")]
    [InlineData(">  q\n> q")]
    [InlineData(">  q\n > q")]
    [InlineData(">  q\n  > q")]
    [InlineData(">  q\n   > q")]
    [InlineData(">  q\n>  q")]
    [InlineData(">  q\n >  q")]
    [InlineData(">  q\n  >  q")]
    [InlineData(">  q\n   >  q")]

    [InlineData(" >  q\n>q")]
    [InlineData(" >  q\n >q")]
    [InlineData(" >  q\n  >q")]
    [InlineData(" >  q\n   >q")]
    [InlineData(" >  q\n> q")]
    [InlineData(" >  q\n > q")]
    [InlineData(" >  q\n  > q")]
    [InlineData(" >  q\n   > q")]
    [InlineData(" >  q\n>  q")]
    [InlineData(" >  q\n >  q")]
    [InlineData(" >  q\n  >  q")]
    [InlineData(" >  q\n   >  q")]

    [InlineData("  >  q\n>q")]
    [InlineData("  >  q\n >q")]
    [InlineData("  >  q\n  >q")]
    [InlineData("  >  q\n   >q")]
    [InlineData("  >  q\n> q")]
    [InlineData("  >  q\n > q")]
    [InlineData("  >  q\n  > q")]
    [InlineData("  >  q\n   > q")]
    [InlineData("  >  q\n>  q")]
    [InlineData("  >  q\n >  q")]
    [InlineData("  >  q\n  >  q")]
    [InlineData("  >  q\n   >  q")]

    [InlineData("   >  q\n>q")]
    [InlineData("   >  q\n >q")]
    [InlineData("   >  q\n  >q")]
    [InlineData("   >  q\n   >q")]
    [InlineData("   >  q\n> q")]
    [InlineData("   >  q\n > q")]
    [InlineData("   >  q\n  > q")]
    [InlineData("   >  q\n   > q")]
    [InlineData("   >  q\n>  q")]
    [InlineData("   >  q\n >  q")]
    [InlineData("   >  q\n  >  q")]
    [InlineData("   >  q\n   >  q")]

    [InlineData(">q\n>q\n>q")]
    [InlineData(">q\n>\n>q")]
    [InlineData(">q\np\n>q")]
    [InlineData(">q\n>\n>\n>q")]
    [InlineData(">q\n>\n>\n>\n>q")]
    [InlineData(">q\n>\n>q\n>\n>q")]
    [InlineData("p\n\n> **q**\n>p\n")]

    [InlineData("> q\np\n> q")] // lazy
    [InlineData("> q\n> q\np")] // lazy

    [InlineData(">>q")]
    [InlineData(" >  >   q")]

    [InlineData("> **q**\n>p\n")]
    [InlineData("> **q**")]
    public void Test(string value)
    {
        RoundTrip(value);
    }

    [Theory]
    [InlineData(">     q")] // 5
    [InlineData(">      q")] // 6
    [InlineData(" >     q")] //5
    [InlineData(" >      q")] //6
    [InlineData(" > \tq")]
    [InlineData(">     q\n>     q")] // 5, 5
    [InlineData(">     q\n>      q")] // 5, 6
    [InlineData(">      q\n>     q")] // 6, 5
    [InlineData(">      q\n>      q")] // 6, 6
    [InlineData(">     q\n\n>     5")] // 5, 5
    public void TestIndentedCodeBlock(string value)
    {
        RoundTrip(value);
    }

    [Theory]
    [InlineData("\n> q")]
    [InlineData("\n> q\n")]
    [InlineData("\n> q\n\n")]
    [InlineData("> q\n\np")]
    [InlineData("p\n\n> q\n\n# h")]

    //https://github.com/lunet-io/markdig/issues/480
    //[TestCase(">\np")]
    //[TestCase(">**b**\n>\n>p\n>\np\n")]
    public void TestParagraph(string value)
    {
        RoundTrip(value);
    }

    [Theory]
    [InlineData("> q\n\n# h\n")]
    public void TestAtxHeader(string value)
    {
        RoundTrip(value);
    }

    [Theory]
    [InlineData(">- i")]
    [InlineData("> - i")]
    [InlineData(">- i\n>- i")]
    [InlineData(">- >p")]
    [InlineData("> - >p")]
    [InlineData(">- i1\n>- i2\n")]
    [InlineData("> **p** p\n>- i1\n>- i2\n")]
    public void TestUnorderedList(string value)
    {
        RoundTrip(value);
    }

    [Theory]
    [InlineData("> *q*\n>p\n")]
    [InlineData("> *q*")]
    public void TestEmphasis(string value)
    {
        RoundTrip(value);
    }

    [Theory]
    [InlineData("> **q**\n>p\n")]
    [InlineData("> **q**")]
    public void TestStrongEmphasis(string value)
    {
        RoundTrip(value);
    }

    [Theory]
    [InlineData(">p\n")]
    [InlineData(">p\r")]
    [InlineData(">p\r\n")]

    [InlineData(">p\n>p")]
    [InlineData(">p\r>p")]
    [InlineData(">p\r\n>p")]

    [InlineData(">p\n>p\n")]
    [InlineData(">p\r>p\n")]
    [InlineData(">p\r\n>p\n")]

    [InlineData(">p\n>p\r")]
    [InlineData(">p\r>p\r")]
    [InlineData(">p\r\n>p\r")]

    [InlineData(">p\n>p\r\n")]
    [InlineData(">p\r>p\r\n")]
    [InlineData(">p\r\n>p\r\n")]
    public void TestNewline(string value)
    {
        RoundTrip(value);
    }

    [Theory]
    [InlineData(">\n>q")]
    [InlineData(">\n>\n>q")]
    [InlineData(">q\n>\n>q")]
    [InlineData(">q\n>\n>\n>q")]
    [InlineData(">q\n> \n>q")]
    [InlineData(">q\n>  \n>q")]
    [InlineData(">q\n>   \n>q")]
    [InlineData(">q\n>\t\n>q")]
    [InlineData(">q\n>\v\n>q")]
    [InlineData(">q\n>\f\n>q")]
    public void TestEmptyLines(string value)
    {
        RoundTrip(value);
    }

    [Theory]
    [InlineData(">> a\nb\n>> c\n")]
    [InlineData(">> a\n        b\n>> c\n")]
    [InlineData("> a\n    b\n> c\n")]
    [InlineData("> - a\n    b\n> c\n")]
    [InlineData("> a\n> b\n> ===\n")]
    [InlineData("> a\n>   b\n> ===\n")]
    [InlineData("> a\nb\n> ===\n")]
    [InlineData("> a\n    b\n> ---\n")]
    public void TestLazyContinuation(string value)
    {
        RoundTrip(value);
    }

    [Theory]
    [InlineData("  >\tq")]
    [InlineData("  >\t")]
    [InlineData("  >\t\n  >\tq")]
    [InlineData("  >\t>\tq")]
    [InlineData("  >\tq\n  >\tq")]
    [InlineData("  > q\n  >\tq")]
    [InlineData("  >\tq\nq")]
    [InlineData("  >\tq\n\tq")]
    [InlineData("  >\t<div>\n  >\t</div>")]
    [InlineData("  >\t```\n  >\tq\n  >\t```")]
    [InlineData("   >\t\tq")]
    public void TestTabConsumedByQuoteMarker(string value)
    {
        RoundTrip(value);
    }

    [Theory]
    [InlineData(">>\n>\n")]
    [InlineData(">>\n>>\n")]
    [InlineData(">>\n>\n>\n")]
    [InlineData("> >\n> \n")]
    [InlineData("> ***\n>\n")]
    [InlineData("> # h\n>\n")]
    [InlineData("> ```\n> x\n> ```\n>\n")]
    [InlineData(">\ny\n")]
    [InlineData(">\n\ny\n")]
    [InlineData(">\r\ny\r\n")]
    [InlineData(">\ry\r")]
    public void TestLinesWithoutContent(string value)
    {
        RoundTrip(value);
    }
}
