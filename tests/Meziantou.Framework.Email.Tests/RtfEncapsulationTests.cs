using System.Text;
using System.Text.RegularExpressions;

namespace Meziantou.Framework.Email.Tests;

/// <summary>Extracts the HTML and plain text bodies encapsulated in RTF ([MS-OXRTFEX]), from the example of the specification and from the items of the test files, which have the HTML property too.</summary>
public sealed class RtfEncapsulationTests
{
    static RtfEncapsulationTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    [Fact]
    public void Usage_Snippet()
    {
        #region RtfEncapsulation_Usage
        var rtf = RtfEncapsulation.EncapsulateHtml("<p>Hello {world}</p>\r\n");
        Assert.Equal(RtfEncapsulationFormat.Html, RtfEncapsulation.GetFormat(rtf));
        Assert.Equal("<p>Hello {world}</p>\r\n", RtfEncapsulation.Decapsulate(rtf));

        // An RTF body that does not encapsulate another format
        Assert.Equal(RtfEncapsulationFormat.None, RtfEncapsulation.GetFormat(@"{\rtf1\ansi Hello}"u8));
        Assert.Null(RtfEncapsulation.Decapsulate(@"{\rtf1\ansi Hello}"u8));
        #endregion
    }

    [Fact]
    public void SpecificationExample_Html()
    {
        // [MS-OXRTFEX] section 3.1. The HTML differs from the source HTML of the example where the RTF of the example
        // differs from it ("This is a bold text", "characters which have", the space before &nbsp;), and starts with the
        // space that follows the color table
        var rtf = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "Rtf", "ms-oxrtfex-example.rtf"));
        Assert.Equal(RtfEncapsulationFormat.Html, RtfEncapsulation.GetFormat(rtf));
        Assert.Equal(
            " <HTML><head>\r\n<style>\r\n<!--\r\n /* Style Definitions */\r\n p.MsoNormal, li.MsoNormal {font-family:Arial;}\r\n-->\r\n</style>\r\n" +
            "\t<!-- This is a HTML comment.\r\nThere is a horizontal tab (%x09) character before the comment, \r\nand some new lines inside the comment. -->\r\n" +
            "</head>\r\n<body>\r\n<p\r\nclass=\"MsoNormal\">Note the line break inside a P tag. <b>This is a bold text</b> </p>\r\n<p class=\"MsoNormal\">\r\n" +
            "This is a normal text with a character references:&nbsp; &lt; &uml;<br>\r\ncharacters which have special meaning in RTF: {}\\<br>\r\n" +
            "</p>\r\n<ol>\r\n    <li class=\"MsoNormal\">This is a list item\r\n</ol>\r\n</body>\r\n</HTML>\r\n",
            RtfEncapsulation.Decapsulate(rtf));
    }

    [Fact]
    public void SpecificationExample_NotEncapsulated()
    {
        // [MS-OXRTFEX] section 3.2: the header has neither \fromhtml1 nor \fromtext
        var rtf = "{\\rtf1\\ANSI\\ansicpg1252\\deff0\\deflang1033{\\fonttbl{\\f0\\fswiss\\fcharset0 Arial;}}\r\n{\\*\\generator Riched20 5.50.99.2050;}\\viewkind4\\uc1\\pard\\f0\\fs20 This is a test e-mail.\\par\r\n}"u8;
        Assert.Equal(RtfEncapsulationFormat.None, RtfEncapsulation.GetFormat(rtf));
    }

    [Theory]
    [InlineData(@"{\rtf1\ansi\fromhtml1 {\*\htmltag <p>}x}", RtfEncapsulationFormat.Html)]
    [InlineData(@"{\rtf1\ansi\fromhtml {\*\htmltag <p>}x}", RtfEncapsulationFormat.None)]
    [InlineData(@"{\rtf1\ansi\fromhtml0 x}", RtfEncapsulationFormat.None)]
    [InlineData(@"{\rtf1\ansi\fromtext x}", RtfEncapsulationFormat.Text)]
    [InlineData(@"{\rtf1\ansi text \fromhtml1 x}", RtfEncapsulationFormat.None)]
    [InlineData(@"{\rtf1\a\b\c\d\e\f\g\h\i\fromhtml1 x}", RtfEncapsulationFormat.None)]
    [InlineData(@"{\rtf1\a\b\c\d\e\f\g\fromhtml1 x}", RtfEncapsulationFormat.Html)]
    [InlineData(@"{\rtf2\fromhtml1 x}", RtfEncapsulationFormat.None)]
    [InlineData(@"", RtfEncapsulationFormat.None)]
    public void GetFormat_Header(string rtf, RtfEncapsulationFormat expected)
    {
        Assert.Equal(expected, RtfEncapsulation.GetFormat(Encoding.ASCII.GetBytes(rtf)));
    }

    [Theory]
    [InlineData(@"{\rtf1\ansi\ansicpg1252\fromhtml1 {\*\htmltag <a href=""x"">}link{\*\htmltag </a>}}", @"<a href=""x"">link</a>")]
    [InlineData(@"{\rtf1\ansi\fromhtml1 {\*\htmltag64}{\*\mhtmltag <img src=""cid:1"">}{\*\htmltag <img src=""a.png"">}}", @"<img src=""a.png"">")]
    [InlineData(@"{\rtf1\ansi\fromhtml1 \htmlrtf {\b bold}\htmlrtf0 text}", "text")]
    [InlineData(@"{\rtf1\ansi\fromhtml1 {\htmlrtf hidden}visible}", "visible")]
    [InlineData(@"{\rtf1\ansi\fromhtml1 {\*\htmltag \lquote\rquote\ldblquote\rdblquote\bullet\endash\emdash\~\_\tab\par}}", "\u2018\u2019\u201C\u201D\u2022\u2013\u2014\u00A0\u00AD\t\r\n")]
    [InlineData(@"{\rtf1\ansi\fromhtml1 {\*\htmltag \u8364?\u-3913?\'e9}}", "\u20AC\uF0B7\u00E9")]
    [InlineData(@"{\rtf1\ansi\fromhtml1 \uc2\u8364\'80\'80 x}", "\u20AC x")]
    [InlineData(@"{\rtf1\ansi\fromhtml1 {\colortbl;\red0\green0\blue0;}{\*\generator x;}{\pict 0102}{\*\unknown y}z}", "z")]
    [InlineData(@"{\rtf1\ansi\fromhtml1 \bin3 {}}x}", "x")]
    public void Decapsulate_Html(string rtf, string expected)
    {
        Assert.Equal(expected, RtfEncapsulation.Decapsulate(Encoding.ASCII.GetBytes(rtf)));
    }

    [Fact]
    public void Decapsulate_CodePageOfFont()
    {
        // The text outside of the \htmltag groups is in the code page of the current font, the text of the groups in the default code page
        var rtf = @"{\rtf1\ansi\ansicpg1252\fromhtml1 {\fonttbl{\f0\fswiss Arial;}{\f1\fnil\fcharset204 Arial;}{\f2\fnil\fcharset128 MS Gothic;}}\f1\'c0\f2\'82\'a0\f0\'c0{\*\htmltag \'c0}}"u8;
        Assert.Equal("\u0410\u3042\u00C0\u00C0", RtfEncapsulation.Decapsulate(rtf));
    }

    [Fact]
    public void Decapsulate_Text()
    {
        var rtf = @"{\rtf1\ansi\ansicpg1252\fromtext \deff0{\fonttbl{\f0\fmodern Courier New;}}\f0\fs20 Line 1\par Line\tab 2\line {\*\htmltag ignored}\htmlrtf kept\htmlrtf0 \{x\}}"u8;
        Assert.Equal("Line 1\r\nLine\t2\r\nkept{x}", RtfEncapsulation.Decapsulate(rtf));
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html><body>\r\n<p>Caf\u00E9 \u20AC {x} \\ \t\u00A0</p>\n\r\u0000\uD83D\uDE00</body></html>")]
    [InlineData(" 1 leading space and digit")]
    public void Encapsulate_RoundTrip(string value)
    {
        var html = RtfEncapsulation.EncapsulateHtml(value);
        Assert.Equal(RtfEncapsulationFormat.Html, RtfEncapsulation.GetFormat(html));
        Assert.Equal(value, RtfEncapsulation.Decapsulate(html));

        var text = RtfEncapsulation.EncapsulateText(value);
        Assert.Equal(RtfEncapsulationFormat.Text, RtfEncapsulation.GetFormat(text));
        Assert.Equal(value, RtfEncapsulation.Decapsulate(text));
    }

    [Fact]
    public void TestFiles_MatchMsgReader()
    {
        // The .msg files whose HTML body is only in the RTF body: MsgReader, an independent reader, extracts the same HTML
        var compared = 0;
        foreach (var path in Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "TestData", "Msg"), "*.msg").Order(StringComparer.Ordinal))
        {
            var bytes = File.ReadAllBytes(path);
            var item = MsgFile.Load(bytes);
            if (item.BodyHtml is not null || item.GetRtfBody() is not { } rtf || RtfEncapsulation.GetFormat(rtf) != RtfEncapsulationFormat.Html)
            {
                continue;
            }

            using var oracle = new MsgReader.Outlook.Storage.Message(new MemoryStream(bytes));
            Assert.Equal(Normalize(oracle.BodyHtml), Normalize(item.GetHtmlBody()));
            compared++;
        }

        Assert.HasCount(6, Enumerable.Range(0, compared));
    }

    [Fact]
    public void MapiMessage_GetHtmlBody_FromRtf()
    {
        var builder = new MapiMessageBuilder { Subject = "RTF only" };
        builder.SetRtfBody(RtfEncapsulation.EncapsulateHtml("<p>Hello</p>"));
        var item = MsgFile.Load(MsgFile.ToByteArray(builder));
        Assert.Null(item.BodyHtml);
        Assert.Equal("<p>Hello</p>", item.GetHtmlBody());
        Assert.Null(item.GetTextBody());
        Assert.Equal("<p>Hello</p>", EmailMessage.FromMapi(item).HtmlBody);

        var text = new MapiMessageBuilder { Subject = "Text" };
        text.SetRtfBody(RtfEncapsulation.EncapsulateText("Hello"));
        var textItem = MsgFile.Load(MsgFile.ToByteArray(text));
        Assert.Equal("Hello", textItem.GetTextBody());
        Assert.Null(textItem.GetHtmlBody());
    }

    // MsgReader trims the HTML, and in 2 files differs in whitespace from [MS-OXRTFEX]: it removes the space that follows the
    // space ending a control word ("{\*\htmltag240   }"), and copies the \line of "\htmlrtf \line \htmlrtf0"
    private static string? Normalize(string? value) => value is null ? null : Regex.Replace(value, @"\s+", "", RegexOptions.None, TimeSpan.FromSeconds(10));
}
