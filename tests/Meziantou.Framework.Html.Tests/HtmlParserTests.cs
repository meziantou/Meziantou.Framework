namespace Meziantou.Framework.Html.Tests;

public class HtmlParserTests
{
    [Fact]
    public void HtmlParser_ShouldCloseIElement()
    {
        var document = new HtmlDocument();
        document.LoadHtml("<p><i>1<i>2</p>");

        var html = document.OuterHtml;
        Assert.Equal("<p><i>1<i>2</i></i></p>", html);
    }

    [Fact]
    public void HtmlParser_ShouldCloseImgElement()
    {
        var document = new HtmlDocument();
        document.LoadHtml("<p><img>1<img>2</p>");

        var html = document.OuterHtml;
        Assert.Equal("<p><img />1<img />2</p>", html);
    }

    [Fact]
    public void HtmlParser_ShouldParseScriptTag()
    {
        var document = new HtmlDocument();
        document.LoadHtml("<script type='text/javascript'>my script</script>");
        var scriptNode = document.SelectSingleNode("//script");
        Assert.NotNull(scriptNode);
        Assert.Equal("my script", scriptNode!.InnerHtml);
        Assert.Equal("text/javascript", scriptNode.GetAttributeValue("type"));
    }

    [Fact]
    public void HtmlParser_ShouldUseBaseAddress()
    {
        var document = new HtmlDocument();
        document.LoadHtml("<div><p>sample1</p><p>sample2</p></div>");
        document.BaseAddress = new Uri("https://www.meziantou.net");
        var absoluteUrl = document.MakeAbsoluteUrl("test.html");
        Assert.Equal("https://www.meziantou.net/test.html", absoluteUrl);
    }

    [Fact]
    public void HtmlParser_ShouldUseBaseElement()
    {
        var document = new HtmlDocument();
        document.LoadHtml("<base href='https://www.meziantou.net'>");
        document.BaseAddress = new Uri("https://www.meziantou.net");
        var absoluteUrl = document.MakeAbsoluteUrl("test.html");
        Assert.Equal("https://www.meziantou.net/test.html", absoluteUrl);
    }

    [Fact]
    public void HtmlParser_ErrorTagNotOpened()
    {
        var document = new HtmlDocument();
        document.LoadHtml("</p>");

        var errors = document.Errors.ToList();

        Assert.Single(errors);
        Assert.Equal(HtmlErrorType.TagNotOpened, errors[0].ErrorType);
    }

    [Fact]
    public void HtmlParser_ErrorDuplicateAttribute()
    {
        var document = new HtmlDocument();
        document.LoadHtml("<p a='a' a='b'></p>");

        var errors = document.Errors.ToList();

        Assert.Single(errors);
        Assert.Equal(HtmlErrorType.DuplicateAttribute, errors[0].ErrorType);
    }

    [Fact]
    public void HtmlParser_ParseComment()
    {
        var document = new HtmlDocument();
        document.LoadHtml("<!--Test-->");
        Assert.NotNull(document.FirstChild);
        Assert.Equal(HtmlNodeType.Comment, document.FirstChild!.NodeType);
    }

    [Fact]
    public void HtmlParser_ReadCharacterSet()
    {
        var html = "<html><head><meta charset='UTF-8'></head></html>";
        using var memoryStream = new MemoryStream(Encoding.UTF8.GetBytes(html));
        var document = new HtmlDocument();
        document.Load(memoryStream);
        Assert.Equal(Encoding.UTF8, document.DetectedEncoding);
    }

    [Fact]
    public void HtmlParser_ReadCharacterSet2()
    {
        var html = "<html><head><meta charset='UTF-8'></head></html>";
        using var memoryStream = new MemoryStream(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(html));
        var document = new HtmlDocument();
        document.Load(memoryStream);
        Assert.Equal(Encoding.UTF8, document.DetectedEncoding);
    }

    [Fact]
    public void HtmlParser_ReadCharacterSetFromMetaHttpEquiv()
    {
        var html = "<html><head><meta http-equiv='Content-Type' content='text/html; charset=UTF-8' /></head></html>";
        using var memoryStream = new MemoryStream(Encoding.UTF8.GetBytes(html));
        var document = new HtmlDocument();
        document.Load(memoryStream);
        Assert.Equal(Encoding.UTF8, document.DetectedEncoding);
    }

    [Theory]
    [InlineData("<!--comment-->", "comment")]
    [InlineData("<!-- a -- b -->", " a -- b ")]
    [InlineData("<!--<div>-->", "<div>")]
    [InlineData("<!--<!-- -->", "<!-- ")]
    // '--!>' also closes a comment
    [InlineData("<!-- x --!>", " x ")]
    [InlineData("<!----!>", "")]
    // empty and abruptly closed comments
    [InlineData("<!---->", "")]
    [InlineData("<!-->", "")]
    [InlineData("<!--->", "")]
    [InlineData("<!----->", "-")]
    // a bare '>' does not close a comment
    [InlineData("<!--a>b-->", "a>b")]
    public void HtmlParser_ParseCommentValue(string html, string expectedValue)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);

        var comment = Assert.Single(document.ChildNodes);
        Assert.Equal(HtmlNodeType.Comment, comment.NodeType);
        Assert.Equal(expectedValue, comment.Value);
    }

    [Fact]
    public void HtmlParser_CommentEndBangIsFollowedByText()
    {
        var document = new HtmlDocument();
        document.LoadHtml("<!-- x --!>after");

        Assert.Collection(
            document.ChildNodes,
            node =>
            {
                Assert.Equal(HtmlNodeType.Comment, node.NodeType);
                Assert.Equal(" x ", node.Value);
            },
            node =>
            {
                Assert.Equal(HtmlNodeType.Text, node.NodeType);
                Assert.Equal("after", node.Value);
            });
    }

    [Theory]
    // an unterminated construct at the end of the document degrades to text and keeps every character
    [InlineData("<!--x")]
    [InlineData("<!-- a")]
    [InlineData("<div<p>x")]
    [InlineData("<")]
    [InlineData("a<")]
    // '<' is only the start of a tag when followed by a tag name
    [InlineData("a < b")]
    [InlineData("a <3 b")]
    [InlineData("1<2 and 3>4")]
    [InlineData("a<<b")]
    [InlineData("<<b>")]
    [InlineData("<>x")]
    [InlineData("a<>b")]
    [InlineData("a</ b")]
    [InlineData("</ p>x")]
    [InlineData("2 < 3 && 4 > 1")]
    public void HtmlParser_TextIsNotAlteredByTheParser(string html)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);

        Assert.Equal(html, document.OuterHtml);
        Assert.All(document.ChildNodes, node => Assert.Equal(HtmlNodeType.Text, node.NodeType));
    }

    [Fact]
    public void HtmlParser_UnterminatedQuotedAttributeValue()
    {
        var document = new HtmlDocument();
        document.LoadHtml("<a b='x");

        var element = document.SelectSingleNode("//a");
        Assert.NotNull(element);
        Assert.Equal("x", element!.GetAttributeValue("b"));
    }

    [Theory]
    [InlineData("<a b=\"")]
    [InlineData("<a b='")]
    [InlineData("<a \"")]
    [InlineData("<a '")]
    public void HtmlParser_DocumentEndingOnAnOpeningQuoteDoesNotThrow(string html)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);

        Assert.NotNull(document.SelectSingleNode("//a"));
    }

    [Fact]
    public void HtmlParser_UnterminatedAttributeValueKeepsEveryCharacter()
    {
        var document = new HtmlDocument();
        document.LoadHtml("<a b=\"unclosed>z</a>");

        var element = document.SelectSingleNode("//a");
        Assert.NotNull(element);
        Assert.Equal("unclosed>z</a>", element!.GetAttributeValue("b"));
    }

    [Fact]
    public void HtmlParser_UnterminatedScriptKeepsItsContent()
    {
        var document = new HtmlDocument();
        document.LoadHtml("<script>var a = 1;");

        var script = document.SelectSingleNode("//script");
        Assert.NotNull(script);
        Assert.Equal("var a = 1;", script!.InnerText);
    }

    [Fact]
    public void HtmlParser_UnterminatedStyleKeepsItsContent()
    {
        var document = new HtmlDocument();
        document.LoadHtml("<style>a{color:red}");

        var style = document.SelectSingleNode("//style");
        Assert.NotNull(style);
        Assert.Equal("a{color:red}", style!.InnerText);
    }

    [Fact]
    public void HtmlParser_UnterminatedCDataKeepsItsContent()
    {
        var document = new HtmlDocument();
        document.LoadHtml("<![CDATA[a]");

        var text = Assert.Single(document.ChildNodes);
        Assert.Equal(HtmlNodeType.Text, text.NodeType);
        Assert.Equal("a]", text.Value);
    }

    [Fact]
    public void HtmlParser_UnterminatedAttributeNameKeepsTheElement()
    {
        var document = new HtmlDocument();
        document.LoadHtml("<div class");

        var element = document.SelectSingleNode("//div");
        Assert.NotNull(element);
        Assert.True(element!.HasAttribute("class"));
    }

    [Theory]
    // the end tag may hold whitespace or attributes, and what follows it must not be lost
    [InlineData("<script>a</script >b", "a")]
    [InlineData("<script>a</script\tfoo>b", "a")]
    [InlineData("<script>a</script\n>b", "a")]
    [InlineData("<script>a</SCRIPT>b", "a")]
    [InlineData("<script>var a = \"1\";</script>b", "var a = \"1\";")]
    public void HtmlParser_ScriptEndTag(string html, string expectedContent)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);

        var script = document.SelectSingleNode("//script");
        Assert.NotNull(script);
        Assert.Equal(expectedContent, script!.InnerText);
        Assert.Equal("b", document.LastChild!.Value);
    }

    [Fact]
    public void HtmlParser_ScriptIsOnlyClosedByItsOwnEndTag()
    {
        var document = new HtmlDocument();
        document.LoadHtml("<script>a</scriptx>b</script>");

        var script = document.SelectSingleNode("//script");
        Assert.NotNull(script);
        Assert.Equal("a</scriptx>b", script!.InnerText);
    }

    [Fact]
    public void HtmlParser_StyleEndTagWithWhitespace()
    {
        var document = new HtmlDocument();
        document.LoadHtml("<style>a</style >b");

        var style = document.SelectSingleNode("//style");
        Assert.NotNull(style);
        Assert.Equal("a", style!.InnerText);
        Assert.Equal("b", document.LastChild!.Value);
    }

    [Fact]
    public void HtmlParser_ScriptTypeIsNotInheritedByNestedScripts()
    {
        var document = new HtmlDocument();
        document.Options.ParsedScriptTypes.Add("text/template");
        document.LoadHtml("<script type=\"text/template\"><script src=\"x\"><b>a</b></script></script>");

        var inner = document.SelectSingleNode("//script/script");
        Assert.NotNull(inner);

        // the inner script has no 'type' attribute: its content is raw text
        var text = Assert.Single(inner!.ChildNodes);
        Assert.Equal(HtmlNodeType.Text, text.NodeType);
        Assert.Equal("<b>a</b>", text.Value);
    }

    [Fact]
    public void HtmlParser_TitleContainsRawText()
    {
        var document = new HtmlDocument();
        document.LoadHtml("<title>a<b</title>x");

        var title = document.SelectSingleNode("//title");
        Assert.NotNull(title);
        Assert.Equal("a<b", title!.InnerText);
        Assert.Equal("x", document.LastChild!.Value);
    }

    [Fact]
    public void HtmlParser_TextAreaContainsRawText()
    {
        var document = new HtmlDocument();
        document.LoadHtml("<textarea><p>hello</textarea>x");

        var textarea = document.SelectSingleNode("//textarea");
        Assert.NotNull(textarea);
        Assert.Equal("<p>hello", textarea!.InnerText);
        Assert.Empty(document.SelectNodes("//p"));
    }

    [Theory]
    // https://html.spec.whatwg.org/multipage/parsing.html#rawtext-state
    [InlineData("iframe")]
    [InlineData("noembed")]
    [InlineData("noframes")]
    [InlineData("noscript")]
    [InlineData("xmp")]
    public void HtmlParser_RawTextElementContainsRawText(string tagName)
    {
        var document = new HtmlDocument();
        document.LoadHtml($"<{tagName}><p>hello</{tagName}>x");

        var element = document.SelectSingleNode($"//{tagName}");
        Assert.NotNull(element);
        Assert.Equal("<p>hello", element!.InnerText);
        Assert.Empty(document.SelectNodes("//p"));
    }

    [Fact]
    public void HtmlParser_PlainTextElementContainsRawTextUntilTheEndOfTheDocument()
    {
        var document = new HtmlDocument();
        document.LoadHtml("<plaintext>a<p>b");

        var element = document.SelectSingleNode("//plaintext");
        Assert.NotNull(element);
        Assert.Equal("a<p>b", element!.InnerText);
        Assert.Empty(document.SelectNodes("//p"));
    }

    [Fact]
    public void HtmlParser_DeeplyNestedDocument()
    {
        const int Depth = 10_000;
        var html = string.Concat(Enumerable.Repeat("<div>", Depth)) + "test" + string.Concat(Enumerable.Repeat("</div>", Depth));

        string? actual = null;
        Exception? exception = null;

        // Parsing and writing a document must not recurse once per level. The work runs on a thread with the
        // stack size Windows uses by default, which is much smaller than the main thread of the other platforms,
        // so a recursive implementation fails here instead of only on Windows.
        var thread = new Thread(() =>
        {
            try
            {
                var document = new HtmlDocument();
                document.LoadHtml(html);
                actual = document.InnerHtml;
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        }, maxStackSize: 1024 * 1024);

        thread.Start();
        thread.Join();

        Assert.Null(exception);
        Assert.Equal(html, actual);
    }

    [Theory]
    // a solidus is part of an unquoted attribute value
    [InlineData("<a href=foo/>", "href", "foo/")]
    [InlineData("<a href=/>", "href", "/")]
    [InlineData("<a href=http://example.com/x>", "href", "http://example.com/x")]
    // an attribute without a value is empty, it does not swallow the rest of the tag
    [InlineData("<a b=>", "b", "")]
    [InlineData("<a b= >", "b", "")]
    // a doubled quote is an escaped quote
    [InlineData("<a b=\"a\"\"b\">", "b", "a\"b")]
    [InlineData("<a b=\"\">", "b", "")]
    [InlineData("<a b=\"\"\"\">", "b", "\"")]
    [InlineData("<a b='a''b'>", "b", "a'b")]
    // a processing instruction ends with '?>'
    [InlineData("<?xml version=\"1.0\"?>", "version", "1.0")]
    [InlineData("<?xml version=\"1.0\" encoding=\"utf-8\"?>", "encoding", "utf-8")]
    public void HtmlParser_ParseAttributeValue(string html, string attributeName, string expectedValue)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);

        var element = (HtmlElement)document.FirstChild!;
        Assert.Equal(expectedValue, element.GetAttributeValue(attributeName));
    }

    [Fact]
    public void HtmlParser_AttributeValueEndsTheTag()
    {
        var document = new HtmlDocument();
        document.LoadHtml("<a b=>z</a>");

        var element = document.SelectSingleNode("//a");
        Assert.NotNull(element);
        Assert.Equal("", element!.GetAttributeValue("b"));
        Assert.Equal("z", element.InnerText);
    }

    [Fact]
    public void HtmlParser_SolidusIsNotPartOfTheTagName()
    {
        var document = new HtmlDocument();
        document.LoadHtml("<br/ >x");

        Assert.NotNull(document.SelectSingleNode("//br"));
        Assert.Equal("x", document.LastChild!.Value);
    }

    [Fact]
    public void HtmlParser_SolidusIsNotPartOfTheAttributeName()
    {
        var document = new HtmlDocument();
        document.LoadHtml("<a b/c>");

        var element = document.SelectSingleNode("//a");
        Assert.NotNull(element);
        Assert.True(element!.HasAttribute("b"));
        Assert.True(element.HasAttribute("c"));
    }

    [Fact]
    public void HtmlParser_EndTagAttributesAreNotAddedToTheParent()
    {
        var document = new HtmlDocument();
        document.LoadHtml("<div><p>a</p class=\"x\"></div>");

        var div = document.SelectSingleNode("//div");
        Assert.NotNull(div);
        Assert.False(div!.HasAttributes);
        Assert.Equal("<div><p>a</p></div>", document.OuterHtml);
    }

    [Theory]
    [InlineData("<p>a<p>b", "<p>a</p><p>b</p>")]
    [InlineData("<p>a<div>b</div>", "<p>a</p><div>b</div>")]
    [InlineData("<p>a<b>c<p>d", "<p>a<b>c</b></p><p>d</p>")]
    [InlineData("<ul><li>1<li>2</ul>", "<ul><li>1</li><li>2</li></ul>")]
    [InlineData("<ul><li><b>x<li>y</ul>", "<ul><li><b>x</b></li><li>y</li></ul>")]
    [InlineData("<dl><dt>a<dd>b<dt>c</dl>", "<dl><dt>a</dt><dd>b</dd><dt>c</dt></dl>")]
    [InlineData("<select><option>1<option>2</select>", "<select><option>1</option><option>2</option></select>")]
    [InlineData("<table><tr><td>a<td>b<tr><td>c</table>", "<table><tr><td>a</td><td>b</td></tr><tr><td>c</td></tr></table>")]
    // already well-formed documents are not altered
    [InlineData("<div><p>a</p><p>b</p></div>", "<div><p>a</p><p>b</p></div>")]
    [InlineData("<ul><li>a</li><li>b</li></ul>", "<ul><li>a</li><li>b</li></ul>")]
    // elements that do not imply an end tag still nest
    [InlineData("<span>a<span>b</span></span>", "<span>a<span>b</span></span>")]
    public void HtmlParser_ImpliedEndTags(string html, string expected)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);

        Assert.Equal(expected, document.OuterHtml);
    }

    [Fact]
    public void HtmlParser_ImpliedEndTagsCanBeConfigured()
    {
        var document = new HtmlDocument();
        document.Options.SetImpliedEndTags("p");
        document.LoadHtml("<p>a<p>b");

        Assert.Equal("<p>a<p>b</p></p>", document.OuterHtml);
    }

    [Theory]
    // the <li> of a nested list does not close the <li> of the outer list
    [InlineData("<ul><li>a<ul><li>b</ul></ul>", "<ul><li>a<ul><li>b</li></ul></li></ul>")]
    // the <td> of a nested table does not close the <td> of the outer table
    [InlineData("<table><tr><td>a<table><tr><td>b</table></table>", "<table><tr><td>a<table><tr><td>b</td></tr></table></td></tr></table>")]
    public void HtmlParser_ImpliedEndTagsDoNotCrossAScopeBoundary(string html, string expected)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);

        Assert.Equal(expected, document.OuterHtml);
    }

    [Fact]
    public void HtmlParser_ParseUnicodeNonCharacter()
    {
        var document = new HtmlDocument();
        document.LoadHtml("x￿y");

        var text = Assert.Single(document.ChildNodes);
        Assert.Equal("x￿y", text.Value);
    }

    [Theory]
    // the '>' eaten after a '/' must still be counted: '<br/>' and '<br >' put '<p>' at the same offset
    [InlineData("<br/><p>ab</p>")]
    [InlineData("<br ><p>ab</p>")]
    public void HtmlReader_TracksPositionsAcrossSelfClosingTags(string html)
    {
        var states = ReadAll(html);

        var text = states.Single(s => s.ParserState == HtmlParserState.Text);
        Assert.Equal("ab", text.Value);

        // the state is pushed when the '<' of '</p>' is read, at offset 10
        Assert.Equal(10, text.Offset);
        Assert.Equal(1, text.Line);
    }

    [Fact]
    public void HtmlReader_TracksOffsets()
    {
        var states = ReadAll("<p a='1'>b</p>");

        Assert.Collection(
            states,
            state => AssertState(state, HtmlParserState.TagOpen, "p", offset: 2),
            state => AssertState(state, HtmlParserState.AttName, "a", offset: 4),
            state => AssertState(state, HtmlParserState.AttValue, "1", offset: 7),
            state => AssertState(state, HtmlParserState.TagEnd, "p", offset: 8),
            state => AssertState(state, HtmlParserState.Text, "b", offset: 10),
            state => AssertState(state, HtmlParserState.TagClose, "p", offset: 13),
            state => AssertState(state, HtmlParserState.TagEnd, "/p", offset: 13));

        static void AssertState(HtmlReaderState state, HtmlParserState parserState, string value, int offset)
        {
            Assert.Equal(parserState, state.ParserState);
            Assert.Equal(value, state.Value);
            Assert.Equal(offset, state.Offset);
        }
    }

    [Theory]
    [InlineData("a\nb", 2)]
    [InlineData("a\r\nb", 2)]
    [InlineData("a\rb", 2)]
    [InlineData("a\r\nb\rc\nd", 4)]
    [InlineData("a\n\nb", 3)]
    public void HtmlReader_CountsLines(string html, int expectedLine)
    {
        var states = ReadAll(html);

        Assert.Equal(expectedLine, states[^1].Line);
    }

    private static List<HtmlReaderState> ReadAll(string html)
    {
        var reader = new HtmlReader(new StringReader(html));
        var states = new List<HtmlReaderState>();
        while (reader.Read())
        {
            states.Add(reader.State);
        }

        return states;
    }
}
