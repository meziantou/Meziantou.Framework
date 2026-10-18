using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Tests.Html;

/// <summary>The commands of <c>document.execCommand()</c> (https://w3c.github.io/editing/docs/execCommand/).</summary>
public sealed class EditingCommandsTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();

    public void Dispose() => _contexts.Dispose();

    /// <summary>
    /// Loads an editing host whose content is <paramref name="html"/>. The selection is given by the markers <c>[</c> and
    /// <c>]</c> in the text (a single <c>[]</c> is a collapsed selection).
    /// </summary>
    private (Document Document, Element Host) Load(string html, string hostAttributes = "")
    {
        var document = _contexts.Create().Navigate("https://example.com/", _ => $"<!DOCTYPE html><div id=host contenteditable {hostAttributes}>{html}</div><p>after</p>");
        _contexts.EventLoop.RunUntilIdle();
        var host = document.GetElementById("host")!;
        (Node Node, uint Offset)? start = null;
        (Node Node, uint Offset)? end = null;
        foreach (var text in host.Descendants().OfType<Text>().ToArray())
        {
            if (text.Data.IndexOf('[', StringComparison.Ordinal) is >= 0 and var startIndex)
            {
                text.DeleteData((uint)startIndex, 1);
                start = (text, (uint)startIndex);
            }

            if (text.Data.IndexOf(']', StringComparison.Ordinal) is >= 0 and var endIndex)
            {
                text.DeleteData((uint)endIndex, 1);
                end = (text, (uint)endIndex);
            }

            // A Text node that only contains the markers is a position in its parent
            if (text.Data.Length == 0)
            {
                var position = (text.ParentNode!, (uint)text.ParentNode!.ChildNodes.ToList().IndexOf(text));
                start = start is { } s && ReferenceEquals(s.Node, text) ? position : start;
                end = end is { } e && ReferenceEquals(e.Node, text) ? position : end;
                text.Remove();
            }
        }

        Assert.NotNull(start);
        end ??= start;
        document.GetSelection()!.SetBaseAndExtent(start.Value.Node, start.Value.Offset, end.Value.Node, end.Value.Offset);
        return (document, host);
    }

    private string Run(string html, string command, string value = "", bool styleWithCss = false, string hostAttributes = "")
    {
        var (document, host) = Load(html, hostAttributes);
        document.ExecCommand("styleWithCSS", showUI: false, styleWithCss ? "true" : "false");
        Assert.True(document.ExecCommand(command, showUI: false, value));
        return host.InnerHtml;
    }

    [Theory]
    [InlineData("foo[bar]baz", "bold", "", "foo<b>bar</b>baz")]
    [InlineData("<b>foo[bar]baz</b>", "bold", "", "<b>foo</b>bar<b>baz</b>")]
    [InlineData("foo[bar]baz", "italic", "", "foo<i>bar</i>baz")]
    [InlineData("foo[bar]baz", "underline", "", "foo<u>bar</u>baz")]
    [InlineData("foo[bar]baz", "strikethrough", "", "foo<strike>bar</strike>baz")]
    [InlineData("foo[bar]baz", "subscript", "", "foo<sub>bar</sub>baz")]
    [InlineData("foo[bar]baz", "superscript", "", "foo<sup>bar</sup>baz")]
    [InlineData("foo[bar]baz", "fontName", "sans-serif", "foo<font face=\"sans-serif\">bar</font>baz")]
    [InlineData("foo[bar]baz", "fontSize", "4", "foo<font size=\"4\">bar</font>baz")]
    [InlineData("<font size=1>foo[bar]baz</font>", "fontSize", "4", "<font size=\"1\">foo</font><font size=\"4\">bar</font><font size=\"1\">baz</font>")]
    [InlineData("foo[bar]baz", "foreColor", "#0000FF", "foo<font color=\"#0000ff\">bar</font>baz")]
    [InlineData("foo[bar]baz", "createLink", "http://www.google.com/", "foo<a href=\"http://www.google.com/\">bar</a>baz")]
    [InlineData("foo<a href=http://www.google.com/>b[a]r</a>baz", "unlink", "", "foobarbaz")]
    [InlineData("foo<b>b[a]r</b><i>baz</i>", "removeFormat", "", "foo<b>b</b>a<b>r</b><i>baz</i>")]
    public void InlineFormatting(string html, string command, string value, string expected)
        => Assert.Equal(expected, Run(html, command, value));

    [Theory]
    [InlineData("foo[bar]baz", "bold", "", "foo<span style=\"font-weight: bold;\">bar</span>baz")]
    [InlineData("foo[bar]baz", "hiliteColor", "#00FFFF", "foo<span style=\"background-color: rgb(0, 255, 255);\">bar</span>baz")]
    [InlineData("foo[bar]baz", "fontSize", "4", "foo<span style=\"font-size: large;\">bar</span>baz")]
    public void InlineFormatting_StyleWithCss(string html, string command, string value, string expected)
        => Assert.Equal(expected, Run(html, command, value, styleWithCss: true));

    [Theory]
    [InlineData("<p>[foo]</p>", "formatBlock", "<h1>", "<h1>foo</h1>")]
    [InlineData("foo[]bar", "formatBlock", "blockquote", "<blockquote>foobar</blockquote>")]
    [InlineData("<p>[foo]</p><p>bar</p>", "indent", "", "<blockquote><p>foo</p></blockquote><p>bar</p>")]
    [InlineData("<blockquote><p>[foo]</p></blockquote>", "outdent", "", "<p>foo</p>")]
    [InlineData("<p>[foo]</p>", "insertOrderedList", "", "<ol><li>foo</li></ol>")]
    [InlineData("<ol><li>[foo]</li></ol>", "insertOrderedList", "", "<div>foo</div>")]
    [InlineData("<ol><li>[foo]</li></ol>", "insertUnorderedList", "", "<ul><li>foo</li></ul>")]
    [InlineData("<p>[foo]</p>", "justifyCenter", "", "<div style=\"text-align: center\"><p>foo</p></div>")]
    [InlineData("<div align=center>[foo]</div>", "justifyLeft", "", "foo")]
    [InlineData("<center>[foo]</center>", "justifyRight", "", "<div style=\"text-align: right\">foo</div>")]
    public void BlockFormatting(string html, string command, string value, string expected)
        => Assert.Equal(expected, Run(html, command, value));

    [Theory]
    [InlineData("foo[]bar", "insertText", "a", "fooabar")]
    [InlineData("foo[bar]baz", "insertText", "a", "fooabaz")]
    [InlineData("<p>foo[]bar</p>", "insertParagraph", "", "<p>foo</p><p>bar</p>")]
    [InlineData("foo[]bar", "insertParagraph", "", "<div>foo</div><div>bar</div>")]
    [InlineData("<h1>foo[]</h1>", "insertParagraph", "", "<h1>foo</h1><div><br></div>")]
    [InlineData("<ul><li>foo</li><li>[]<br></li></ul>", "insertParagraph", "", "<ul><li>foo</li></ul><div><br></div>")]
    [InlineData("foo[]bar", "insertLineBreak", "", "foo<br>bar")]
    [InlineData("foo[]", "insertLineBreak", "", "foo<br><br>")]
    [InlineData("foo[]bar", "insertHTML", "<b>a</b>", "foo<b>a</b>bar")]
    [InlineData("<p>foo[]bar</p>", "insertHTML", "<p>a</p>", "<p>foo</p><p>a</p><p>bar</p>")]
    [InlineData("foo[bar]baz", "insertImage", "/img.png", "foo<img src=\"/img.png\">baz")]
    [InlineData("<b>foo[bar</b><i>baz]quz</i>", "insertImage", "/img.png", "<b>foo</b><img src=\"/img.png\"><i>quz</i>")]
    [InlineData("<span>foo[</span><span>]bar</span>", "insertImage", "/img.png", "<span>foo</span><img src=\"/img.png\"><span>bar</span>")]
    [InlineData("<b>[]foo</b>", "insertImage", "/img.png", "<img src=\"/img.png\"><b>foo</b>")]
    [InlineData("<p>foo[]bar</p>", "insertHorizontalRule", "", "<p>foo</p><hr><p>bar</p>")]
    public void Insertion(string html, string command, string value, string expected)
        => Assert.Equal(expected, Run(html, command, value));

    [Theory]
    [InlineData("foo[]bar", "delete", "fobar")]
    [InlineData("foo[]bar", "forwardDelete", "fooar")]
    [InlineData("foo[bar]baz", "delete", "foobaz")]
    [InlineData("<p>foo</p><p>[]bar</p>", "delete", "<p>foobar</p>")]
    [InlineData("<p>foo[]</p><p>bar</p>", "forwardDelete", "<p>foobar</p>")]
    [InlineData("<p>fo[o</p><p>b]ar</p>", "delete", "<p>foar</p>")]
    [InlineData("<p>[foo]</p>", "delete", "<p><br></p>")]
    [InlineData("<p><b>[foo]</b></p>", "delete", "<p><br></p>")]
    [InlineData("<ol><li>foo</li><li>[]bar</li></ol>", "delete", "<ol><li>foobar</li></ol>")]
    [InlineData("foo<img src=/img.png>[]bar", "delete", "foobar")]
    [InlineData("foo<span contenteditable=false>bar</span>[]baz", "delete", "foobaz")]
    [InlineData("<div style=\"white-space:normal\">abc</div><div style=\"white-space:pre\">[]def\nghi</div>", "delete", "<div style=\"white-space:normal\">abc<span style=\"white-space: pre;\">def</span></div><div style=\"white-space:pre\">ghi</div>")]
    public void Deletion(string html, string command, string expected)
        => Assert.Equal(expected, Run(html, command));

    [Fact]
    public void Deletion_CanonicalizesTheWhiteSpace()
    {
        // The space that would be collapsed at the end of the line becomes a no-break space
        Assert.Equal("foo&nbsp;", Run("foo b[]", "delete"));
        Assert.Equal("foo bar", Run("foo b[]bar", "forwardDelete"));
    }

    [Fact]
    public void CollapsedSelection_StateOverride()
    {
        var (document, host) = Load("foo[]bar");
        Assert.False(document.QueryCommandState("bold"));
        Assert.True(document.ExecCommand("bold"));
        Assert.Equal("foobar", host.InnerHtml);
        Assert.True(document.QueryCommandState("bold"));

        Assert.True(document.ExecCommand("insertText", showUI: false, "a"));
        Assert.Equal("foo<b>a</b>bar", host.InnerHtml);
        Assert.True(document.QueryCommandState("bold"));
    }

    [Fact]
    public void QueryCommands()
    {
        var (document, _) = Load("<p style=\"text-align: right\"><b>fo[o</b><i><font face=sans-serif size=5 color=#ff0000>b]ar</font></i></p>");
        Assert.True(document.QueryCommandIndeterm("bold"));
        Assert.False(document.QueryCommandState("bold"));
        Assert.True(document.QueryCommandIndeterm("italic"));
        Assert.True(document.QueryCommandIndeterm("fontName"));
        Assert.Equal("p", document.QueryCommandValue("formatBlock"));
        Assert.True(document.QueryCommandState("justifyRight"));
        Assert.Equal("right", document.QueryCommandValue("justifyCenter"));
        Assert.False(document.QueryCommandState("insertOrderedList"));

        document.GetSelection()!.SelectAllChildren(document.QuerySelector("font")!);
        Assert.False(document.QueryCommandState("bold"));
        Assert.True(document.QueryCommandState("italic"));
        Assert.Equal("sans-serif", document.QueryCommandValue("fontName"));
        Assert.Equal("5", document.QueryCommandValue("fontSize"));
        Assert.Equal("rgb(255, 0, 0)", document.QueryCommandValue("foreColor"));
        Assert.Equal("", document.QueryCommandValue("bold"));
        Assert.Equal("", document.QueryCommandValue("unknown"));
    }

    [Fact]
    public void MiscellaneousCommands()
    {
        var (document, host) = Load("foo[]bar");
        Assert.Equal("div", document.QueryCommandValue("defaultParagraphSeparator"));
        Assert.False(document.ExecCommand("defaultParagraphSeparator", showUI: false, "span"));
        Assert.True(document.ExecCommand("defaultParagraphSeparator", showUI: false, "P"));
        Assert.Equal("p", document.QueryCommandValue("defaultParagraphSeparator"));
        Assert.True(document.ExecCommand("insertParagraph"));
        Assert.Equal("<p>foo</p><p>bar</p>", host.InnerHtml);

        Assert.False(document.QueryCommandState("styleWithCSS"));
        Assert.True(document.ExecCommand("useCSS", showUI: false, "false"));
        Assert.True(document.QueryCommandState("styleWithCSS"));

        // The miscellaneous commands are enabled without selection in an editing host
        document.GetSelection()!.RemoveAllRanges();
        Assert.True(document.QueryCommandEnabled("styleWithCSS"));
        Assert.False(document.QueryCommandEnabled("bold"));
        Assert.False(document.ExecCommand("bold"));
        Assert.True(document.QueryCommandSupported("undo"));
        Assert.False(document.QueryCommandSupported("heading"));
    }

    [Fact]
    public void InvalidValues()
    {
        var (document, host) = Load("foo[bar]baz");
        Assert.False(document.ExecCommand("fontSize", showUI: false, "abc"));
        Assert.False(document.ExecCommand("formatBlock", showUI: false, "<span>"));
        Assert.False(document.ExecCommand("createLink", showUI: false, ""));
        Assert.False(document.ExecCommand("insertImage", showUI: false, ""));
        Assert.False(document.ExecCommand("foreColor", showUI: false, "currentColor"));
        Assert.Equal("foobarbaz", host.InnerHtml);
    }

    [Fact]
    public void InputEvents()
    {
        var (document, host) = Load("foo[bar]baz");
        var events = new List<string>();
        host.AddEventListener("beforeinput", e => events.Add("beforeinput"));
        host.AddEventListener("input", e => events.Add(((InputEvent)e).InputType + ":" + ((InputEvent)e).Data));

        Assert.True(document.ExecCommand("bold"));
        Assert.True(document.ExecCommand("fontName", showUI: false, "serif"));
        Assert.True(document.ExecCommand("insertText", showUI: false, "a"));
        Assert.True(document.ExecCommand("insertParagraph"));
        Assert.True(document.ExecCommand("delete"));
        Assert.True(document.ExecCommand("formatBlock", showUI: false, "p"));
        Assert.True(document.ExecCommand("styleWithCSS", showUI: false, "true"));
        Assert.Equal(["formatBold:", "formatFontName:serif", "insertText:a", "insertParagraph:", "deleteContentBackward:", ":"], events);
    }

    [Fact]
    public void Commands_DoNotEditOutsideTheEditingHost()
    {
        var (document, host) = Load("[foo]", "style=\"display: inline\"");
        Assert.True(document.ExecCommand("insertParagraph"));
        Assert.Same(document.Body, host.ParentNode);
        Assert.Equal("<br><br>", host.InnerHtml);
        Assert.Equal("after", document.QuerySelector("p")!.TextContent);
    }
}
