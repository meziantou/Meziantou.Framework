using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Tests.Html;

/// <summary>
/// The insertion and formatting commands of <c>document.execCommand()</c> where the browsers are followed instead of the
/// editing specification (https://w3c.github.io/editing/docs/execCommand/): see the "Editing" section of docs/browsing-context.md.
/// </summary>
public sealed class EditingInsertionTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();

    public void Dispose() => _contexts.Dispose();

    /// <summary>
    /// Loads an editing host (a <paramref name="hostName"/> element) whose content is <paramref name="html"/>. The
    /// selection is given by the markers <c>[</c> and <c>]</c> in the text (a single <c>[]</c> is a collapsed selection).
    /// </summary>
    private (Document Document, Element Host) Load(string html, string hostName = "div", string hostAttributes = "")
    {
        var document = _contexts.Create().Navigate("https://example.com/", _ => $"<!DOCTYPE html><body></body>");
        _contexts.EventLoop.RunUntilIdle();
        var host = document.CreateElement(hostName);
        host.SetAttribute("contenteditable", "");
        if (hostAttributes.Length > 0)
        {
            host.SetAttribute("style", hostAttributes);
        }

        document.Body!.AppendChild(host);
        host.InnerHtml = html;
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

    private string Run(string html, params string[] commands)
    {
        var (document, host) = Load(html);
        Execute(document, commands);
        return host.InnerHtml;
    }

    /// <summary>Runs the commands: <c>name</c> or <c>name=value</c>.</summary>
    private static void Execute(Document document, string[] commands)
    {
        foreach (var command in commands)
        {
            var separator = command.IndexOf('=', StringComparison.Ordinal);
            var name = separator < 0 ? command : command[..separator];
            var value = separator < 0 ? "" : command[(separator + 1)..];
            Assert.True(document.ExecCommand(name, showUI: false, value), command);
        }
    }

    [Theory]
    // The inline elements of the caret are cloned in the line that becomes empty
    [InlineData("<p><b>foo[]</b></p>", "<p><b>foo</b></p><p><b><br></b></p>")]
    [InlineData("<p><b>[]foo</b></p>", "<p><b><br></b></p><p><b>foo</b></p>")]
    [InlineData("<p><b><i>foo[]</i></b></p>", "<p><b><i>foo</i></b></p><p><b><i><br></i></b></p>")]
    [InlineData("<div><span style=\"color:rgb(0, 0, 255)\">foo[]</span><br></div>", "<div><span style=\"color:rgb(0, 0, 255)\">foo</span></div><div><span style=\"color:rgb(0, 0, 255)\"><br></span></div>")]
    [InlineData("<p><b id=x>foo[]</b></p>", "<p><b id=\"x\">foo</b></p><p><b><br></b></p>")]
    // The formatting of a caret in a link is not kept
    [InlineData("<p><a href=foo>foo[]</a></p>", "<p><a href=\"foo\">foo</a></p><p><br></p>")]
    [InlineData("<p><a href=foo><b>foo[]</b></a></p>", "<p><a href=\"foo\"><b>foo</b></a></p><p><br></p>")]
    // The inline elements that are split are cloned by the split, as in the specification
    [InlineData("<p><b>fo[]o</b></p>", "<p><b>fo</b></p><p><b>o</b></p>")]
    // The space of a side of the split becomes a no-break space, the other white space is left as it is
    [InlineData("<div>a []b</div>", "<div>a&nbsp;</div><div>b</div>")]
    [InlineData("<div>a[] b</div>", "<div>a</div><div>&nbsp;b</div>")]
    [InlineData("<div>a&nbsp;&nbsp;[]&nbsp;&nbsp;&nbsp;b</div>", "<div>a&nbsp;&nbsp;</div><div>&nbsp;&nbsp;&nbsp;b</div>")]
    [InlineData("<div>abc[] </div>", "<div>abc</div><div><br></div>")]
    // The br that ends the line before the line that is wrapped is kept
    [InlineData("foo<br>ba[]r", "foo<br><div>ba</div><div>r</div>")]
    // A single-line container that is inline-level is a content of its line
    [InlineData("<div style=\"display:inline\">a[]bc</div>", "<div><div style=\"display:inline\">a</div></div><div><div style=\"display:inline\">bc</div></div>")]
    public void InsertParagraph(string html, string expected)
    {
        Assert.Equal(expected, Run(html, "insertParagraph"));
    }

    [Theory]
    [InlineData("span", "", "a[]b", "a<br>b")]
    [InlineData("span", "", "abcd[]", "abcd<br><br>")]
    [InlineData("p", "", "<div style=\"display:inline\">a[]b</div>", "<div style=\"display:inline\">a<br>b</div>")]
    [InlineData("p", "", "<div style=\"display:inline-block\">a[]b</div>", "<div style=\"display:inline-block\">a<br>b</div>")]
    [InlineData("p", "white-space:pre-wrap", "<div style=\"display:inline\">a[]b</div>", "<div style=\"display:inline\">a\nb</div>")]
    [InlineData("p", "", "<div>a[]b</div>", "<div>a</div><div>b</div>")]
    public void InsertParagraph_InHostThatCannotContainTheSeparator(string hostName, string hostStyle, string html, string expected)
    {
        var (document, host) = Load(html, hostName, hostStyle);
        Assert.True(document.ExecCommand("insertParagraph", showUI: false, ""));
        Assert.Equal(expected, host.InnerHtml);
    }

    [Fact]
    public void InsertParagraph_AfterTheHostWhenItIsFollowedByText()
    {
        // The line break that ends an inline editing host needs another one, whatever follows the host
        var (document, host) = Load("abcd[]", "span");
        host.ParentNode!.AppendChild(document.CreateTextNode("efg"));
        Assert.True(document.ExecCommand("insertParagraph", showUI: false, ""));
        Assert.Equal("abcd<br><br>", host.InnerHtml);
    }

    [Theory]
    [InlineData("tr")]
    [InlineData("tbody")]
    [InlineData("table")]
    public void InsertParagraph_CaretInTablePart(string selector)
    {
        var (document, host) = Load("<div>x[]<table><tbody><tr><td>abc</td></tr></tbody></table></div>");
        ((Text)host.FirstChild!.FirstChild!).Remove();
        document.GetSelection()!.Collapse(host.QuerySelector(selector), 0);
        Assert.True(document.ExecCommand("insertParagraph", showUI: false, ""));
        Assert.Equal("<div><br></div><div><table><tbody><tr><td>abc</td></tr></tbody></table></div>", host.InnerHtml);
    }

    [Fact]
    public void InsertParagraph_CaretInVoidElement()
    {
        var (document, host) = Load("<div>abc[]<hr></div>");
        document.GetSelection()!.Collapse(host.QuerySelector("hr"), 0);
        Assert.True(document.ExecCommand("insertParagraph", showUI: false, ""));
        Assert.Equal("<div>abc</div><div><hr></div>", host.InnerHtml);
    }

    [Theory]
    // The text typed in the new paragraph keeps the formatting of the caret
    [InlineData("<div><br>[]</div>", new[] { "fontName=monospace", "insertText=abc", "insertParagraph", "insertText=def" }, "<div><font face=\"monospace\">abc</font></div><div><font face=\"monospace\">def</font></div>")]
    [InlineData("<p><b>foo[]</b></p>", new[] { "insertParagraph", "insertText=a" }, "<p><b>foo</b></p><p><b>a</b></p>")]
    // ... but not the formatting of the block (the bold of a heading)
    [InlineData("<h3>abc[]</h3>", new[] { "insertParagraph", "insertText=a" }, "<h3>abc</h3><div>a</div>")]
    // The formatting of the text of the start of the selection that is deleted is restored
    [InlineData("<div>abc<b>[def</b>]ghi</div>", new[] { "insertParagraph", "insertText=d", "insertText=e" }, "<div>abc</div><div><b>de</b>ghi</div>")]
    public void InsertParagraph_ThenInsertText(string html, string[] commands, string expected)
    {
        Assert.Equal(expected, Run(html, commands));
    }

    [Theory]
    // The text typed at the boundaries of a link is not a part of the link
    [InlineData("<p><a href=\"about:blank\">abc[]</a></p>", "<p><a href=\"about:blank\">abc</a>XY</p>")]
    [InlineData("<p><a href=\"about:blank\">[]abc</a></p>", "<p>XY<a href=\"about:blank\">abc</a></p>")]
    [InlineData("<p><a href=\"about:blank\">a[]bc</a></p>", "<p><a href=\"about:blank\">aXYbc</a></p>")]
    [InlineData("<p><b><a href=\"about:blank\">abc[]</a></b></p>", "<p><b><a href=\"about:blank\">abc</a>XY</b></p>")]
    // The text that replaces a selection in a link is in the link, as the text typed after it
    [InlineData("<p><a href=\"about:blank\">abc[d]</a></p>", "<p><a href=\"about:blank\">abcXY</a></p>")]
    [InlineData("<p><a href=\"about:blank\">[abcd]</a></p>", "<p><a href=\"about:blank\">XY</a></p>")]
    // ... but not the text that replaces a selection that goes out of the link
    [InlineData("<p><a href=\"about:blank\">ab[cd</a>de]f</p>", "<p><a href=\"about:blank\">ab</a>XYf</p>")]
    [InlineData("<p>foo<a href=\"about:blank\">[bar</a>baz]</p>", "<p>fooXY</p>")]
    // No link is created for the URLs
    [InlineData("<p>http://a[]</p>", "<p>http://aXY</p>")]
    public void InsertText_AroundLinks(string html, string expected)
    {
        Assert.Equal(expected, Run(html, "insertText=X", "insertText=Y"));
    }

    [Fact]
    public void InsertText_AfterDeletionAtTheStartOfLink()
    {
        Assert.Equal("<p>X<a href=\"about:blank\">abc</a></p>", Run("<p><a href=\"about:blank\">z[]abc</a></p>", "delete", "insertText=X"));
    }

    [Fact]
    public void InsertText_SpaceAfterUrlCreatesNoLink()
    {
        Assert.Equal("<p>http://a&nbsp;</p>", Run("<p>http://a[]</p>", "insertText= "));
    }

    [Theory]
    // The br that keeps the line of the caret rendered is replaced with the text
    [InlineData("abc<br>[]<br>", "abc<br>d")]
    [InlineData("<p>[]<br></p>", "<p>d</p>")]
    [InlineData("<div style=\"white-space:pre\">[]\n</div>", "<div style=\"white-space:pre\">d</div>")]
    public void InsertText_ReplacesThePaddingLineBreak(string html, string expected)
    {
        Assert.Equal(expected, Run(html, "insertText=d"));
    }

    [Fact]
    public void InsertText_CaretInVoidElement()
    {
        var (document, host) = Load("<div>abc[]<hr>def</div>");
        document.GetSelection()!.Collapse(host.QuerySelector("hr"), 0);
        Assert.True(document.ExecCommand("insertText", showUI: false, "X"));
        Assert.Equal("<div>abcX<hr>def</div>", host.InnerHtml);
    }

    [Fact]
    public void InsertText_EmptyStringAtCaretDoesNothing()
    {
        Assert.Equal("a&nbsp;&nbsp;b", Run("a[]&nbsp;&nbsp;b", "insertText="));
    }

    [Theory]
    [InlineData("insertText", "X")]
    [InlineData("delete", "")]
    [InlineData("forwardDelete", "")]
    public void CommandsDoNothingInSelectElement(string command, string value)
    {
        var (document, host) = Load("<p>abc[]</p><select><option>def</option></select>");
        document.GetSelection()!.Collapse(host.QuerySelector("option")!.FirstChild, 1);
        Assert.False(document.ExecCommand(command, showUI: false, value));
        Assert.Equal("<p>abc</p><select><option>def</option></select>", host.InnerHtml);
    }

    [Theory]
    // The inline elements of the caret are split: the fragment does not get their formatting
    [InlineData("<i><b>a[]c</b></i>", "<u>b</u>", "<i><b>a</b></i><u>b</u><i><b>c</b></i>")]
    [InlineData("<b>[]def</b>", "<i>abc</i>", "<i>abc</i><b>def</b>")]
    [InlineData("<b>abc[]</b>", "<i>def</i>", "<b>abc</b><i>def</i>")]
    // The text is inserted in place
    [InlineData("<b>a[]c</b>", "b", "<b>abc</b>")]
    // The white space around the caret is left as it is
    [InlineData("a[]&nbsp;&nbsp;&nbsp;b", "<i>x</i>", "a<i>x</i>&nbsp;&nbsp;&nbsp;b")]
    public void InsertHtml(string html, string value, string expected)
    {
        Assert.Equal(expected, Run(html, "insertHTML=" + value));
    }

    [Theory]
    // The value is forced on the contents of the inline element that the range is in
    [InlineData("abc<i>[def]</i>ghi", "bold", "abc<i><b>def</b></i>ghi")]
    [InlineData("<b>[abc]</b>", "italic", "<b><i>abc</i></b>")]
    [InlineData("foo<u>[bar]</u>baz", "strikethrough", "foo<u><strike>bar</strike></u>baz")]
    [InlineData("<p><b>[abc]</b></p>", "createLink=about:blank", "<p><b><a href=\"about:blank\">abc</a></b></p>")]
    // ... but on the element when the range is around it, or contains other nodes, as in the specification
    [InlineData("<span>[foo</span> <span>bar]</span>", "bold", "<b><span>foo</span> <span>bar</span></b>")]
    // The font elements stay around the other inline elements
    [InlineData("<b>[abc]</b>", "foreColor=#ff0000", "<font color=\"#ff0000\"><b>abc</b></font>")]
    [InlineData("<b>[abc]</b>", "fontSize=5", "<font size=\"5\"><b>abc</b></font>")]
    public void InlineFormatting_Nesting(string html, string command, string expected)
    {
        Assert.Equal(expected, Run(html, "styleWithCSS=false", command));
    }
}
