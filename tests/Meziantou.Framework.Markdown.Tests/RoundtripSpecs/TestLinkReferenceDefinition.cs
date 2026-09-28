using Meziantou.Framework.Markdown.Renderers.Roundtrip;
using static Meziantou.Framework.Markdown.Tests.TestRoundtrip;

namespace Meziantou.Framework.Markdown.Tests.RoundtripSpecs;

public class TestLinkReferenceDefinition
{
    [Theory]
    [InlineData(@"[a]: /r")]
    [InlineData(@" [a]: /r")]
    [InlineData(@"  [a]: /r")]
    [InlineData(@"   [a]: /r")]

    [InlineData(@"[a]:  /r")]
    [InlineData(@" [a]:  /r")]
    [InlineData(@"  [a]:  /r")]
    [InlineData(@"   [a]:  /r")]

    [InlineData(@"[a]:  /r ")]
    [InlineData(@" [a]:  /r ")]
    [InlineData(@"  [a]:  /r ")]
    [InlineData(@"   [a]:  /r ")]

    [InlineData(@"[a]: /r ""l""")]
    [InlineData(@"[a]:  /r ""l""")]
    [InlineData(@"[a]: /r  ""l""")]
    [InlineData(@"[a]: /r ""l"" ")]
    [InlineData(@"[a]:  /r  ""l""")]
    [InlineData(@"[a]:  /r  ""l"" ")]

    [InlineData(@" [a]: /r ""l""")]
    [InlineData(@" [a]:  /r ""l""")]
    [InlineData(@" [a]: /r  ""l""")]
    [InlineData(@" [a]: /r ""l"" ")]
    [InlineData(@" [a]:  /r  ""l""")]
    [InlineData(@" [a]:  /r  ""l"" ")]

    [InlineData(@"  [a]: /r ""l""")]
    [InlineData(@"  [a]:  /r ""l""")]
    [InlineData(@"  [a]: /r  ""l""")]
    [InlineData(@"  [a]: /r ""l"" ")]
    [InlineData(@"  [a]:  /r  ""l""")]
    [InlineData(@"  [a]:  /r  ""l"" ")]

    [InlineData(@"   [a]: /r ""l""")]
    [InlineData(@"   [a]:  /r ""l""")]
    [InlineData(@"   [a]: /r  ""l""")]
    [InlineData(@"   [a]: /r ""l"" ")]
    [InlineData(@"   [a]:  /r  ""l""")]
    [InlineData(@"   [a]:  /r  ""l"" ")]

    [InlineData("[a]:\t/r")]
    [InlineData("[a]:\t/r\t")]
    [InlineData("[a]:\t/r\t\"l\"")]
    [InlineData("[a]:\t/r\t\"l\"\t")]

    [InlineData("[a]: \t/r")]
    [InlineData("[a]: \t/r\t")]
    [InlineData("[a]: \t/r\t\"l\"")]
    [InlineData("[a]: \t/r\t\"l\"\t")]

    [InlineData("[a]:\t /r")]
    [InlineData("[a]:\t /r\t")]
    [InlineData("[a]:\t /r\t\"l\"")]
    [InlineData("[a]:\t /r\t\"l\"\t")]

    [InlineData("[a]: \t /r")]
    [InlineData("[a]: \t /r\t")]
    [InlineData("[a]: \t /r\t\"l\"")]
    [InlineData("[a]: \t /r\t\"l\"\t")]

    [InlineData("[a]:\t/r \t")]
    [InlineData("[a]:\t/r \t\"l\"")]
    [InlineData("[a]:\t/r \t\"l\"\t")]

    [InlineData("[a]: \t/r \t")]
    [InlineData("[a]: \t/r \t\"l\"")]
    [InlineData("[a]: \t/r \t\"l\"\t")]

    [InlineData("[a]:\t /r \t")]
    [InlineData("[a]:\t /r \t\"l\"")]
    [InlineData("[a]:\t /r \t\"l\"\t")]

    [InlineData("[a]: \t /r \t")]
    [InlineData("[a]: \t /r \t\"l\"")]
    [InlineData("[a]: \t /r \t\"l\"\t")]

    [InlineData("[a]:\t/r\t ")]
    [InlineData("[a]:\t/r\t \"l\"")]
    [InlineData("[a]:\t/r\t \"l\"\t")]

    [InlineData("[a]: \t/r\t ")]
    [InlineData("[a]: \t/r\t \"l\"")]
    [InlineData("[a]: \t/r\t \"l\"\t")]

    [InlineData("[a]:\t /r\t ")]
    [InlineData("[a]:\t /r\t \"l\"")]
    [InlineData("[a]:\t /r\t \"l\"\t")]

    [InlineData("[a]: \t /r\t ")]
    [InlineData("[a]: \t /r\t \"l\"")]
    [InlineData("[a]: \t /r\t \"l\"\t")]

    [InlineData("[a]:\t/r \t ")]
    [InlineData("[a]:\t/r \t \"l\"")]
    [InlineData("[a]:\t/r \t \"l\"\t")]

    [InlineData("[a]: \t/r \t ")]
    [InlineData("[a]: \t/r \t \"l\"")]
    [InlineData("[a]: \t/r \t \"l\"\t")]

    [InlineData("[a]:\t /r \t ")]
    [InlineData("[a]:\t /r \t \"l\"")]
    [InlineData("[a]:\t /r \t \"l\"\t")]

    [InlineData("[a]: \t /r \t ")]
    [InlineData("[a]: \t /r \t \"l\"")]
    [InlineData("[a]: \t /r \t \"l\"\t")]
    public void Test(string value)
    {
        RoundTrip(value);
    }

    [Theory]
    [InlineData("[a]: /r\n[b]: /r\n")]
    [InlineData("[a]: /r\n[b]: /r\n[c] /r\n")]
    public void TestMultiple(string value)
    {
        RoundTrip(value);
    }

    [Theory]
    [InlineData("[a]:\f/r\f\"l\"")]
    [InlineData("[a]:\v/r\v\"l\"")]
    public void TestUncommonWhitespace(string value)
    {
        RoundTrip(value);
    }

    [Theory]
    [InlineData("[a]:\n/r\n\"t\"")]
    [InlineData("[a]:\n/r\r\"t\"")]
    [InlineData("[a]:\n/r\r\n\"t\"")]

    [InlineData("[a]:\r/r\n\"t\"")]
    [InlineData("[a]:\r/r\r\"t\"")]
    [InlineData("[a]:\r/r\r\n\"t\"")]

    [InlineData("[a]:\r\n/r\n\"t\"")]
    [InlineData("[a]:\r\n/r\r\"t\"")]
    [InlineData("[a]:\r\n/r\r\n\"t\"")]

    [InlineData("[a]:\n/r\n\"t\nt\"")]
    [InlineData("[a]:\n/r\n\"t\rt\"")]
    [InlineData("[a]:\n/r\n\"t\r\nt\"")]

    [InlineData("[a]:\r\n  /r\t \n \t \"t\r\nt\"   ")]
    [InlineData("[a]:\n/r\n\n[a],")]
    [InlineData("[a]: /r\n[b]: /r\n\n[a],")]
    public void TestNewlines(string value)
    {
        RoundTrip(value);
    }

    [Theory]
    [InlineData("[ a]: /r")]
    [InlineData("[a ]: /r")]
    [InlineData("[ a ]: /r")]
    [InlineData("[  a]: /r")]
    [InlineData("[  a ]: /r")]
    [InlineData("[a  ]: /r")]
    [InlineData("[ a  ]: /r")]
    [InlineData("[  a  ]: /r")]
    [InlineData("[a a]: /r")]
    [InlineData("[a\va]: /r")]
    [InlineData("[a\fa]: /r")]
    [InlineData("[a\ta]: /r")]
    [InlineData("[\va]: /r")]
    [InlineData("[\fa]: /r")]
    [InlineData("[\ta]: /r")]
    [InlineData(@"[\]]: /r")]
    public void TestLabel(string value)
    {
        RoundTrip(value);
    }

    [Theory]
    [InlineData("[a]: /r ()")]
    [InlineData("[a]: /r (t)")]
    [InlineData("[a]: /r ( t)")]
    [InlineData("[a]: /r (t )")]
    [InlineData("[a]: /r ( t )")]

    [InlineData("[a]: /r ''")]
    [InlineData("[a]: /r 't'")]
    [InlineData("[a]: /r ' t'")]
    [InlineData("[a]: /r 't '")]
    [InlineData("[a]: /r ' t '")]
    public void Test_Title(string value)
    {
        RoundTrip(value);
    }

    [Theory]
    [InlineData("[a]: /r\n'b\n")]
    [InlineData("[a]: /r\n'b\n[a]\n")]
    [InlineData("[a]: /r \n  \"b\n")]
    [InlineData("[a]: /r\r\n(b\r\n")]
    [InlineData("> [a]: /r\n> 'b\n")]
    public void TestNextLineIsNotATitle(string value)
    {
        RoundTrip(value);
    }

    [Theory]
    [InlineData("[link]: https://example.com\n'Tis the season.\n\n[link]\n")]
    [InlineData("> [link]: https://example.com\n>   'Tis the season.\n")]
    [InlineData("[a]: /r\ntext\n\nx\n")]
    [InlineData("[a]: /r\n'b\n\n")]
    [InlineData("[a]: /r\n\"b\" c\n\nx\n")]
    [InlineData("[a]: /r  \n\"b\" c\n")]
    [InlineData("[a]: /r\n[b]: /s\ntext\n\n\nx\n")]
    [InlineData("\n\n[a]: /r\n[b]: /s\n\nx\n")]
    [InlineData("\n\n[a]: /r\ntext\n")]
    [InlineData("- [a]: /r\n  text\n\n- x\n")]
    [InlineData("> [a]: /r\n>   text\n")]
    [InlineData("> [a]: /r\n>\ttext\n")]
    [InlineData("> [a]: /r\n>   \"b\" c\n")]
    [InlineData("> - [a]: /r\n>   text\n")]
    [InlineData("> [a]: /r\n===\n\nx\n")]
    [InlineData("- [a]: /r\n===\n\nx\n")]
    public void TestParagraphAfterDefinition(string value)
    {
        RoundTrip(value);
    }

    [Theory]
    [InlineData("> [a]:\r> /r\r")]
    [InlineData("> [a]:\r> /r\r> x\r")]
    [InlineData("> [a]:\n>   /r\n      x\n")]
    [InlineData("> [a]:\n>   /r\n    x\n> y\n")]
    [InlineData(">> [a]:\n        /r\n>> [b]: <b>\n")]
    [InlineData("> - [a]:\r\n    b\"\r> \"c\" d\r")]
    [InlineData("> [a]: /r \"b\n> c\"\n>   x\n")]
    public void TestMultilineInContainer(string value)
    {
        RoundTrip(value);
    }

    [Theory]
    [InlineData("[a]: /r\n===\n[a]")]
    public void TestSetextHeader(string value)
    {
        RoundTrip(value);
    }

    [Theory]
    [InlineData("> [foo]: /url 'the title'\n")]
    [InlineData("> [foo]: /url\n> 'the title'\n")]
    [InlineData("> [foo]:\n> /url 'the title'\n")]
    [InlineData("> [foo]:\n> /url\n> 'the title'\n")]
    [InlineData("> [foo]:\n> /url\n>\n> [foo]\n")]
    [InlineData("> [foo]:\n> /url 'the title'\n>\n> [foo]\n")]
    [InlineData("> [foo]:\n> /url\n> 'the title'\n>\n> [foo]\n")]
    public void TestMultilineInBlockquote(string value)
    {
        RoundTrip(value);
    }

    [Theory]
    [InlineData("text\n\n[^1]: x\n")]
    [InlineData("[^1]: x")]
    [InlineData("a[^1]\n\n[^1]: x\n")]
    [InlineData("a[^1]\n\n[^1]: x\n    y\n")]
    [InlineData("a[^1]\n\n[^1]: x\ny\n")]
    [InlineData("a[^2] b[^1]\n\n[^1]: x\n[^2]: y\n")]
    [InlineData("a[^1]\n\n[^1]: x\n\ntext\n")]
    [InlineData("[^1]: x\n\n[a]: /u\n\n[a] [^1]\n")]
    [InlineData("[a]: /u\n\n[^1]: x\n\n[a] [^1]\n")]
    [InlineData("a[^1]\n\n[^1]:\n    x\n\n    y\n")]
    [InlineData("a[^1]\n\n[^1]: - x\n    - y\n")]
    [InlineData("a [^1] b [^1]\n\n  [^1]:  x  \n")]
    [InlineData("a[^1]\r\n\r\n[^1]: x\r\n")]
    [InlineData("a[^1]\r\r[^1]: x\r")]
    [InlineData("[^1]: a\n\n\n[^2]: b\n")]
    [InlineData("[^1]: a\n    ```\n    code\n    ```\n")]
    [InlineData("[^1]:   \r\n\r\n")]
    [InlineData("[^a]: x\n\n[^A] [^ a ]\n")]
    public void TestFootnote(string value)
    {
        var pipeline = new MarkdownPipelineBuilder().UseFootnotes().EnableTrackTrivia().Build();
        var writer = new StringWriter();
        var renderer = new RoundtripRenderer(writer);
        pipeline.Setup(renderer);
        renderer.Write(MarkdownConverter.Parse(value, pipeline));

        Assert.Equal(value, writer.ToString());
    }
}
