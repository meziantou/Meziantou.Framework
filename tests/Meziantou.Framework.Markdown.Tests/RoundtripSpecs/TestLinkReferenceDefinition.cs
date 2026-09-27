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
}
