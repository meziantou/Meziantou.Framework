using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Tests.Html;

/// <summary>
/// The delete and forwardDelete commands, and the Backspace and Delete keys, in editable content: the behaviors of the
/// browsers that differ from the editing specification (https://w3c.github.io/editing/docs/execCommand/#the-delete-command).
/// </summary>
public sealed class EditingDeletionTests : IDisposable
{
    private const string NoBreakSpace = "\U000000A0";

    private readonly BrowsingContextScope _contexts = new();

    public void Dispose() => _contexts.Dispose();

    /// <summary>
    /// Loads an editing host whose content is <paramref name="html"/>. The selection is given by the markers <c>[</c> and
    /// <c>]</c> in the text (a single <c>[]</c> is a collapsed selection; a Text node that only contains markers is a
    /// position in its parent).
    /// </summary>
    private (Document Document, Element Host) Load(string html, string head = "")
    {
        var document = _contexts.Create().Navigate("https://example.com/", _ => $"<!DOCTYPE html>{head}<div id=host contenteditable>{html}</div><p>after</p>");
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

    private string Run(string html, string command, string head = "")
    {
        var (document, host) = Load(html, head);
        Assert.True(document.ExecCommand(command, showUI: false, ""));
        return host.InnerHtml;
    }

    private static string Describe(Node node, uint offset) => $"{(node is Text text ? "\"" + text.Data + "\"" : node.NodeName)}:{offset}";

    private static string DescribeSelection(Document document)
    {
        var range = document.GetSelection()!.GetRangeAt(0);
        return range.Collapsed ? Describe(range.StartContainer, range.StartOffset) : $"{Describe(range.StartContainer, range.StartOffset)}-{Describe(range.EndContainer, range.EndOffset)}";
    }

    /// <summary>The target ranges of the beforeinput event of a deletion of the user.</summary>
    private static string GetTargetRanges(Element host, string inputType)
    {
        var ranges = "";
        host.AddEventListener("beforeinput", e => ranges = string.Join(",", ((InputEvent)e).GetTargetRanges().Select(range => $"{Describe(range.StartContainer, range.StartOffset)}-{Describe(range.EndContainer, range.EndOffset)}")));
        ((HtmlElement)host).Focus();
        Editing.UserEdit(host, inputType, data: null);
        return ranges;
    }

    // The white space around the deletion is rebalanced in the Text node of the caret only: a single space is a space, the
    // spaces of a sequence alternate with no-break spaces (from a no-break space), and a space that starts or ends the
    // Text node is a no-break space
    [Theory]
    [InlineData("a&nbsp;&nbsp;[]&nbsp;&nbsp;b", "delete", "a&nbsp; &nbsp;b")]
    [InlineData("a&nbsp;&nbsp;[]b", "delete", "a b")]
    [InlineData("a &nbsp; []&nbsp;b", "delete", "a&nbsp; &nbsp;b")]
    [InlineData("a&nbsp;[]&nbsp;&nbsp;", "forwardDelete", "a&nbsp;&nbsp;")]
    [InlineData("a&nbsp;&nbsp;[]&nbsp;&nbsp;&nbsp;", "forwardDelete", "a&nbsp; &nbsp;&nbsp;")]
    [InlineData("&nbsp;[]&nbsp;&nbsp;b", "forwardDelete", "&nbsp; b")]
    [InlineData("foo b[]", "delete", "foo&nbsp;")]
    [InlineData("foo  []bar", "delete", "foobar")]
    [InlineData("<span>abc&nbsp;</span><span>&nbsp;[]def</span>", "delete", "<span>abc&nbsp;</span><span>def</span>")]
    [InlineData("<span>abc&nbsp;[]</span><span> &nbsp; def</span>", "delete", "<span>abc</span><span>&nbsp; &nbsp;def</span>")]
    [InlineData("<span>abc[]&nbsp;<span> def</span></span>", "forwardDelete", "<span>abc<span>&nbsp;def</span></span>")]
    [InlineData("a<span style=\"white-space:pre\">b[]   </span>c", "delete", "a<span style=\"white-space:pre\">   </span>c")]
    [InlineData("a<img src=/img.png>[]&nbsp;&nbsp;&nbsp;b", "delete", "a&nbsp; &nbsp;b")]
    public void WhiteSpaceIsRebalancedInTheTextNodeOfTheCaret(string html, string command, string expected)
        => Assert.Equal(expected, Run(html, command));

    // The white space of the lines that a deletion joins is rebalanced as the one of a single Text node
    [Theory]
    [InlineData("<div>a&nbsp;&nbsp;&nbsp;</div><div>[]&nbsp;&nbsp;&nbsp;b</div>", "delete", "<div>a&nbsp; &nbsp; &nbsp; b</div>")]
    [InlineData("<div>a[]</div><div>&nbsp;&nbsp;&nbsp;b</div>", "forwardDelete", "<div>a&nbsp; &nbsp;b</div>")]
    [InlineData("<div>a&nbsp;&nbsp;&nbsp;[]</div><div><br></div>", "forwardDelete", "<div>a&nbsp; &nbsp;</div>")]
    public void WhiteSpaceOfJoinedLinesIsRebalanced(string html, string command, string expected)
        => Assert.Equal(expected, Run(html, command));

    [Fact]
    public void CaretIsInTheTextNodeAfterADeletion()
    {
        var (document, host) = Load("&nbsp;[]b");
        Assert.True(document.ExecCommand("delete"));
        Assert.Equal("b", host.InnerHtml);
        Assert.Equal("\"b\":0", DescribeSelection(document));

        (document, host) = Load("a&nbsp;[]&nbsp;b");
        Assert.True(document.ExecCommand("forwardDelete"));
        Assert.Equal("a b", host.TextContent);
        Assert.Equal("\"a b\":2", DescribeSelection(document));

        // The caret at the start of a Text node is at the end of the Text node that precedes it
        (document, host) = Load("ab[]");
        var second = ((Text)host.FirstChild!).SplitText(1);
        document.GetSelection()!.Collapse(second, 0);
        Assert.True(document.ExecCommand("forwardDelete"));
        Assert.Equal("a", host.TextContent);
        Assert.Equal("\"a\":1", DescribeSelection(document));
    }

    // A br element keeps the line that a deletion empties when other contents of its block precede or follow it
    [Theory]
    [InlineData("a[]<p>bc</p>", "delete", "<br><p>bc</p>")]
    [InlineData("<p>abc</p>d[]", "delete", "<p>abc</p><br>")]
    [InlineData("<p>abc</p>[]d<p>efg</p>", "forwardDelete", "<p>abc</p><br><p>efg</p>")]
    [InlineData("<div style=\"white-space:pre\">foo\nb[]</div>", "delete", "<div style=\"white-space:pre\">foo\n<br></div>")]
    [InlineData("<p>foo</p>[<p>bar</p>]<p>baz</p>", "delete", "<p>foo</p><p>baz</p>")]
    [InlineData("<p>foo</p><br><p>[]bar</p>", "delete", "<p>foo</p><p>bar</p>")]
    public void EmptiedLineKeepsALineBreak(string html, string command, string expected)
        => Assert.Equal(expected, Run(html, command));

    // The white-space of a pre element is kept in a span when its line is moved to another block only when an author
    // style declares it (the user agent style sheet does not count)
    [Theory]
    [InlineData("", "<p>abc<span style=\"white-space: pre;\">def</span></p>")]
    [InlineData("<style>pre { white-space: pre; }</style>", "<p>abc<span style=\"white-space: pre;\">def</span></p>")]
    public void JoinedLineOfPreKeepsTheWhiteSpaceOfTheAuthor(string head, string expected)
    {
        Assert.Equal(head.Length == 0 ? "<p>abcdef</p>" : expected, Run("<p>abc</p><pre>[]def</pre>", "delete", head));
        Assert.Equal(head.Length == 0 ? "<p>abcdef</p><pre>ghi</pre>" : expected + "<pre>ghi</pre>", Run("<p>abc[]</p><pre>def\nghi</pre>", "forwardDelete", head));
        Assert.Equal("<pre>abcdef</pre>", Run("<pre>abc</pre><p>[]def</p>", "delete", head));
    }

    [Fact]
    public void CascadeOriginOfTheWinningDeclaration()
    {
        var document = _contexts.Create().Navigate("https://example.com/", _ => "<!DOCTYPE html><style>.a { white-space: pre-wrap } #c { color: red }</style><pre id=a></pre><pre id=b class=a></pre><div id=c></div><div id=d style='white-space: nowrap'></div>");
        _contexts.EventLoop.RunUntilIdle();
        var engine = document.GetStyleEngine();
        Assert.Equal(CascadeOrigin.UserAgent, engine.GetCascadeOrigin(document.GetElementById("a")!, "white-space-collapse"));
        Assert.Equal(CascadeOrigin.Author, engine.GetCascadeOrigin(document.GetElementById("b")!, "white-space-collapse"));
        Assert.Null(engine.GetCascadeOrigin(document.GetElementById("c")!, "white-space-collapse"));
        Assert.Equal(CascadeOrigin.Author, engine.GetCascadeOrigin(document.GetElementById("c")!, "color"));
        Assert.Equal(CascadeOrigin.Author, engine.GetCascadeOrigin(document.GetElementById("d")!, "text-wrap-mode"));
        Assert.Null(engine.GetCascadeOrigin(document.GetElementById("d")!, "color"));
    }

    // Backspace at the start of a blockquote joins its first line to the line before it; the other indentation elements are outdented
    [Theory]
    [InlineData("foo<blockquote>[]bar</blockquote>", "foobar")]
    [InlineData("<pre>abc</pre><blockquote>[]def<br>ghi</blockquote>", "<pre>abcdef</pre><blockquote>ghi</blockquote>")]
    [InlineData("<blockquote>[]bar</blockquote>", "bar")]
    [InlineData("foo<blockquote><blockquote>[]bar</blockquote></blockquote>", "foo<blockquote>bar</blockquote>")]
    [InlineData("foo<blockquote style=\"color: blue\">[]bar</blockquote>", "foo<div style=\"color: blue\">bar</div>")]
    public void BackspaceAtTheStartOfABlockquote(string html, string expected)
        => Assert.Equal(expected, Run(html, "delete"));

    // A deletion that starts at the start of a line and ends in a child block deletes the lines: the block keeps its first line
    [Theory]
    [InlineData("<br>[abc<br><div id=\"child\">]def<br>ghi</div>", "delete", "<br><div id=\"child\">def<br>ghi</div>")]
    [InlineData("[abc<br><br><div id=\"child\">d]ef<br>ghi</div>", "forwardDelete", "<div id=\"child\">ef<br>ghi</div>")]
    [InlineData("ab[c<div id=\"child\">]def<br>ghi</div>", "delete", "abdef<div id=\"child\">ghi</div>")]
    [InlineData("abc<br>[]<br><div id=\"child\">def</div>", "forwardDelete", "abc<br><div id=\"child\">def</div>")]
    [InlineData("<b>[]<br></b><div id=\"child\">def</div>", "forwardDelete", "<div id=\"child\">def</div>")]
    [InlineData("<b><br></b><div id=\"child\">[]def</div>", "delete", "<div id=\"child\">def</div>")]
    [InlineData("<b>abc<br><br></b><div id=\"child\">[]def</div>", "delete", "<b>abc<br></b><div id=\"child\">def</div>")]
    [InlineData("abc<br><div id=\"child\">[]def</div>", "delete", "abcdef")]
    public void LinesBeforeAChildBlockAreDeletedWithoutUnwrappingIt(string html, string command, string expected)
        => Assert.Equal(expected, Run(html, command));

    [Fact]
    public void InsertionAfterTheDeletionOfTheLinesBeforeAChildBlockIsInTheBlock()
    {
        var (document, host) = Load("<br>[abc<br><div id=\"child\">]def</div>");
        Assert.True(document.ExecCommand("insertText", showUI: false, "X"));
        Assert.Equal("<br><div id=\"child\">Xdef</div>", host.InnerHtml);
    }

    // The li elements are joined as the other blocks, and the item of a nested list keeps its position
    [Theory]
    [InlineData("<ol><li>foo</li><li>[]bar</li></ol>", "<ol><li>foobar</li></ol>")]
    [InlineData("<ol><li>foo<br></li><li>[]bar</li></ol>", "<ol><li>foobar</li></ol>")]
    [InlineData("<ol><li><p>foo</p></li><li>[]bar</li></ol>", "<ol><li><p>foobar</p></li></ol>")]
    [InlineData("<ol><li>[foo</li><ul><li>]bar</li></ul></ol>", "<ol><ul><li>bar</li></ul></ol>")]
    [InlineData("<ol><li>foo</li><li>[bar</li><ul><li>baz</li><li>]quz</li></ul></ol>", "<ol><li>foo</li><ul><li>quz</li></ul></ol>")]
    [InlineData("<ol><ul><li>[foo</li></ul><li>]bar</li></ol>", "<ol><ul><li>bar</li></ul></ol>")]
    public void ListItems(string html, string expected)
        => Assert.Equal(expected, Run(html, "delete"));

    [Fact]
    public void RangeAroundTheLastListItemsKeepsTheFirstItem()
    {
        var (document, host) = Load("<ol><li>foo</li><li>bar[]</li></ol>");
        var list = host.FirstChild!;
        document.GetSelection()!.SetBaseAndExtent(list, 1, list, 2);
        Assert.Equal("\"bar\":0-\"bar\":3", GetTargetRanges(host, "deleteContentBackward"));
        Assert.Equal("<ol><li>foo</li><li><br></li></ol>", host.InnerHtml);

        // The item is removed when another item follows it
        (document, host) = Load("<ol><li>foo</li><li>bar[]</li></ol>");
        list = host.FirstChild!;
        document.GetSelection()!.SetBaseAndExtent(list, 0, list, 1);
        Assert.Equal("OL:0-OL:1", GetTargetRanges(host, "deleteContentForward"));
        Assert.Equal("<ol><li>bar</li></ol>", host.InnerHtml);

        (document, host) = Load("<ul><li>abc</li><li>def[]</li></ul>");
        document.GetSelection()!.SetBaseAndExtent(host, 0, host, 1);
        Assert.True(document.ExecCommand("delete"));
        Assert.Equal("<ul><li><br></li></ul>", host.InnerHtml);
    }

    [Theory]
    [InlineData("<p>abc<br>[]def</p>", "deleteContentBackward", "P:1-P:2", "<p>abcdef</p>")]
    [InlineData("<p>abc[]<br>def</p>", "deleteContentForward", "P:1-P:2", "<p>abcdef</p>")]
    [InlineData("<p>abc<img src=/img.png>[]def</p>", "deleteContentBackward", "P:1-P:2", "<p>abcdef</p>")]
    [InlineData("<p>abc[]<img src=/img.png><img src=/img.png>def</p>", "deleteContentForward", "P:1-P:2", "<p>abc<img src=\"/img.png\">def</p>")]
    [InlineData("<p>ab\U0001F600[]c</p>", "deleteContentBackward", "\"ab\U0001F600c\":2-\"ab\U0001F600c\":4", "<p>abc</p>")]
    [InlineData("<p>e\U00000301[]</p>", "deleteContentBackward", "\"e\U00000301\":1-\"e\U00000301\":2", "<p>e</p>")]
    [InlineData("<p>[abc]</p>", "deleteContentBackward", "\"abc\":0-\"abc\":3", "<p><br></p>")]
    public void TargetRangesOfADeletion(string html, string inputType, string expectedRanges, string expectedHtml)
    {
        var (_, host) = Load(html);
        Assert.Equal(expectedRanges, GetTargetRanges(host, inputType));
        Assert.Equal(expectedHtml, host.InnerHtml);
    }

    [Fact]
    public void TargetRangeOfASelectionAroundATextNodeIsInTheTextNode()
    {
        var (document, host) = Load("<p>abc[]</p>");
        var paragraph = host.FirstChild!;
        document.GetSelection()!.SetBaseAndExtent(paragraph, 0, paragraph, 1);
        Assert.Equal("\"abc\":0-\"abc\":3", GetTargetRanges(host, "deleteContentForward"));
        Assert.Equal("<p><br></p>", host.InnerHtml);
    }

    [Fact]
    public void NoBreakSpaceOfTheOtherTextNodesIsKept()
    {
        var (document, host) = Load($"a{NoBreakSpace}{NoBreakSpace}{NoBreakSpace}b[]{NoBreakSpace}{NoBreakSpace}{NoBreakSpace}{NoBreakSpace}c");
        var first = (Text)host.FirstChild!;
        var second = first.SplitText(5);
        document.GetSelection()!.Collapse(first, 5);
        Assert.True(document.ExecCommand("delete"));

        // The white space is rebalanced in the Text node that follows the caret: the Text node before it is not changed
        Assert.Equal($"a{NoBreakSpace}{NoBreakSpace}{NoBreakSpace}", first.Data);
        Assert.Equal($"{NoBreakSpace} {NoBreakSpace} c", second.Data);
        Assert.Equal($"\"{first.Data}\":4", DescribeSelection(document));
    }
}
