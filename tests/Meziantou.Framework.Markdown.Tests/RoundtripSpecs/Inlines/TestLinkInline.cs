using static Meziantou.Framework.Markdown.Tests.TestRoundtrip;

namespace Meziantou.Framework.Markdown.Tests.RoundtripSpecs.Inlines;

public class TestLinkInline
{
    [Theory]
    [InlineData("[a]")] // TODO: this is not a link but a paragraph
    [InlineData("[a]()")]

    [InlineData("[](b)")]
    [InlineData(" [](b)")]
    [InlineData("[](b) ")]
    [InlineData(" [](b) ")]

    [InlineData("[a](b)")]
    [InlineData(" [a](b)")]
    [InlineData("[a](b) ")]
    [InlineData(" [a](b) ")]

    [InlineData("[ a](b)")]
    [InlineData(" [ a](b)")]
    [InlineData("[ a](b) ")]
    [InlineData(" [ a](b) ")]

    [InlineData("[a ](b)")]
    [InlineData(" [a ](b)")]
    [InlineData("[a ](b) ")]
    [InlineData(" [a ](b) ")]

    [InlineData("[ a ](b)")]
    [InlineData(" [ a ](b)")]
    [InlineData("[ a ](b) ")]
    [InlineData(" [ a ](b) ")]

    // below cases are required for a full roundtrip but not have low prio for impl
    [InlineData("[]( b)")]
    [InlineData(" []( b)")]
    [InlineData("[]( b) ")]
    [InlineData(" []( b) ")]

    [InlineData("[a]( b)")]
    [InlineData(" [a]( b)")]
    [InlineData("[a]( b) ")]
    [InlineData(" [a]( b) ")]

    [InlineData("[ a]( b)")]
    [InlineData(" [ a]( b)")]
    [InlineData("[ a]( b) ")]
    [InlineData(" [ a]( b) ")]

    [InlineData("[a ]( b)")]
    [InlineData(" [a ]( b)")]
    [InlineData("[a ]( b) ")]
    [InlineData(" [a ]( b) ")]

    [InlineData("[ a ]( b)")]
    [InlineData(" [ a ]( b)")]
    [InlineData("[ a ]( b) ")]
    [InlineData(" [ a ]( b) ")]

    [InlineData("[](b )")]
    [InlineData(" [](b )")]
    [InlineData("[](b ) ")]
    [InlineData(" [](b ) ")]

    [InlineData("[a](b )")]
    [InlineData(" [a](b )")]
    [InlineData("[a](b ) ")]
    [InlineData(" [a](b ) ")]

    [InlineData("[ a](b )")]
    [InlineData(" [ a](b )")]
    [InlineData("[ a](b ) ")]
    [InlineData(" [ a](b ) ")]

    [InlineData("[a ](b )")]
    [InlineData(" [a ](b )")]
    [InlineData("[a ](b ) ")]
    [InlineData(" [a ](b ) ")]

    [InlineData("[ a ](b )")]
    [InlineData(" [ a ](b )")]
    [InlineData("[ a ](b ) ")]
    [InlineData(" [ a ](b ) ")]

    [InlineData("[]( b )")]
    [InlineData(" []( b )")]
    [InlineData("[]( b ) ")]
    [InlineData(" []( b ) ")]

    [InlineData("[a]( b )")]
    [InlineData(" [a]( b )")]
    [InlineData("[a]( b ) ")]
    [InlineData(" [a]( b ) ")]

    [InlineData("[ a]( b )")]
    [InlineData(" [ a]( b )")]
    [InlineData("[ a]( b ) ")]
    [InlineData(" [ a]( b ) ")]

    [InlineData("[a ]( b )")]
    [InlineData(" [a ]( b )")]
    [InlineData("[a ]( b ) ")]
    [InlineData(" [a ]( b ) ")]

    [InlineData("[ a ]( b )")]
    [InlineData(" [ a ]( b )")]
    [InlineData("[ a ]( b ) ")]
    [InlineData(" [ a ]( b ) ")]
    public void Test(string value)
    {
        RoundTrip(value);
    }

    [Theory]
    [InlineData("[a](b \"t\") ")]
    [InlineData("[a](b \" t\") ")]
    [InlineData("[a](b \"t \") ")]
    [InlineData("[a](b \" t \") ")]

    [InlineData("[a](b  \"t\") ")]
    [InlineData("[a](b  \" t\") ")]
    [InlineData("[a](b  \"t \") ")]
    [InlineData("[a](b  \" t \") ")]

    [InlineData("[a](b \"t\" ) ")]
    [InlineData("[a](b \" t\" ) ")]
    [InlineData("[a](b \"t \" ) ")]
    [InlineData("[a](b \" t \" ) ")]

    [InlineData("[a](b  \"t\" ) ")]
    [InlineData("[a](b  \" t\" ) ")]
    [InlineData("[a](b  \"t \" ) ")]
    [InlineData("[a](b  \" t \" ) ")]

    [InlineData("[a](b 't') ")]
    [InlineData("[a](b ' t') ")]
    [InlineData("[a](b 't ') ")]
    [InlineData("[a](b ' t ') ")]

    [InlineData("[a](b  't') ")]
    [InlineData("[a](b  ' t') ")]
    [InlineData("[a](b  't ') ")]
    [InlineData("[a](b  ' t ') ")]

    [InlineData("[a](b 't' ) ")]
    [InlineData("[a](b ' t' ) ")]
    [InlineData("[a](b 't ' ) ")]
    [InlineData("[a](b ' t ' ) ")]

    [InlineData("[a](b  't' ) ")]
    [InlineData("[a](b  ' t' ) ")]
    [InlineData("[a](b  't ' ) ")]
    [InlineData("[a](b  ' t ' ) ")]

    [InlineData("[a](b (t)) ")]
    [InlineData("[a](b ( t)) ")]
    [InlineData("[a](b (t )) ")]
    [InlineData("[a](b ( t )) ")]

    [InlineData("[a](b  (t)) ")]
    [InlineData("[a](b  ( t)) ")]
    [InlineData("[a](b  (t )) ")]
    [InlineData("[a](b  ( t )) ")]

    [InlineData("[a](b (t) ) ")]
    [InlineData("[a](b ( t) ) ")]
    [InlineData("[a](b (t ) ) ")]
    [InlineData("[a](b ( t ) ) ")]

    [InlineData("[a](b  (t) ) ")]
    [InlineData("[a](b  ( t) ) ")]
    [InlineData("[a](b  (t ) ) ")]
    [InlineData("[a](b  ( t ) ) ")]
    public void Test_Title(string value)
    {
        RoundTrip(value);
    }

    [Theory]
    [InlineData("[a](<>)")]
    [InlineData("[a]( <>)")]
    [InlineData("[a](<> )")]
    [InlineData("[a]( <> )")]

    [InlineData("[a](< >)")]
    [InlineData("[a]( < >)")]
    [InlineData("[a](< > )")]
    [InlineData("[a]( < > )")]

    [InlineData("[a](<b>)")]
    [InlineData("[a](<b >)")]
    [InlineData("[a](< b>)")]
    [InlineData("[a](< b >)")]

    [InlineData("[a](<b b>)")]
    [InlineData("[a](<b b >)")]
    [InlineData("[a](< b b >)")]
    public void Test_PointyBrackets(string value)
    {
        RoundTrip(value);
    }

    [Theory]
    [InlineData("[*a*][a]")]
    [InlineData("[a][b]")]
    [InlineData("[a][]")]
    [InlineData("[a]")]
    public void Test_Inlines(string value)
    {
        RoundTrip(value);
    }

    // | [ a ]( b " t " ) |
    [Theory]
    [InlineData(" [ a ]( b \" t \" ) ")]
    [InlineData("\v[\va\v](\vb\v\"\vt\v\"\v)\v")]
    [InlineData("\f[\fa\f](\fb\f\"\ft\f\"\f)\f")]
    [InlineData("\t[\ta\t](\tb\t\"\tt\t\"\t)\t")]
    public void Test_UncommonWhitespace(string value)
    {
        RoundTrip(value);
    }

    [Theory]
    [InlineData("[x]: https://example.com\r\n")]
    public void Test_LinkReferenceDefinitionWithCarriageReturnLineFeed(string value)
    {
        RoundTrip(value);
    }

    [Theory]
    [InlineData("> [a](/u \"t\n> x\")\n")]
    [InlineData("> [a](/u\n> \"t\"\n>  )\n")]
    [InlineData("> [a](\n>   /u)\n")]
    public void TestSpanningLinesInContainer(string value)
    {
        RoundTrip(value);
    }
}
