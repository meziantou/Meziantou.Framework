using System.Text;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Tests.Html;

/// <summary>
/// The plain text editing hosts (<c>contenteditable=plaintext-only</c>) and the content that is pasted in editable content
/// (WPT editing/plaintext-only, editing/other/paste-*).
/// </summary>
public sealed class EditingPlaintextOnlyTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly SystemClipboard _clipboard = new();

    public void Dispose() => _contexts.Dispose();

    /// <summary>
    /// Loads <paramref name="html"/>, whose editing host has the id <c>host</c>. The selection is given by the markers
    /// <c>[</c> and <c>]</c> in the text, if any (a single <c>[]</c> is a collapsed selection).
    /// </summary>
    private (Document Document, Element Host) LoadDocument(string html)
    {
        var context = _contexts.Create(new BrowsingContextOptions { Clipboard = _clipboard, ClipboardPermission = (_, _) => true });
        var document = context.Navigate("https://example.com/", _ => "<!DOCTYPE html>" + html);
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

        if (start is not null)
        {
            end ??= start;
            document.GetSelection()!.SetBaseAndExtent(start.Value.Node, start.Value.Offset, end.Value.Node, end.Value.Offset);
        }

        return (document, host);
    }

    /// <summary>Loads an editing host whose content is <paramref name="html"/>.</summary>
    private (Document Document, Element Host) Load(string html, string contentEditable = "plaintext-only", string whiteSpace = "normal")
        => LoadDocument($"<div id=host contenteditable=\"{contentEditable}\" style=\"white-space: {whiteSpace}\">{html}</div><p>after</p>");

    private void WriteClipboard(string? text, string? html)
    {
        var representations = new List<SystemClipboardRepresentation>();
        if (text is not null)
        {
            representations.Add(new SystemClipboardRepresentation("text/plain", Encoding.UTF8.GetBytes(text)));
        }

        if (html is not null)
        {
            representations.Add(new SystemClipboardRepresentation("text/html", Encoding.UTF8.GetBytes(html)));
        }

        _clipboard.Write([new SystemClipboardItem(representations)]);
    }

    private static string Normalize(string html) => html.Replace("&nbsp;", " ", StringComparison.Ordinal);

    [Theory]
    [InlineData("a[b]c", "backColor", "red")]
    [InlineData("a[b]c", "bold", "")]
    [InlineData("a[b]c", "createLink", "http://example.com")]
    [InlineData("a[b]c", "fontName", "Arial")]
    [InlineData("a[b]c", "fontSize", "7")]
    [InlineData("a[b]c", "foreColor", "red")]
    [InlineData("a[b]c", "formatBlock", "p")]
    [InlineData("a[b]c", "hiliteColor", "red")]
    [InlineData("<ul><li>[abc]</li></ul>", "indent", "")]
    [InlineData("a[b]c", "insertHorizontalRule", "")]
    [InlineData("a[b]c", "insertImage", "image.png")]
    [InlineData("a[b]c", "insertOrderedList", "")]
    [InlineData("a[b]c", "insertUnorderedList", "")]
    [InlineData("a[b]c", "italic", "")]
    [InlineData("<div>[abc]</div>", "justifyCenter", "")]
    [InlineData("<blockquote>[abc]</blockquote>", "outdent", "")]
    [InlineData("a<b>[b]</b>c", "removeFormat", "")]
    [InlineData("a[b]c", "strikeThrough", "")]
    [InlineData("a[b]c", "subscript", "")]
    [InlineData("a[b]c", "superscript", "")]
    [InlineData("a[b]c", "underline", "")]
    [InlineData("a<a href=\"http://example.com\">[b]</a>c", "unlink", "")]
    public void FormattingCommands_AreNotEnabled(string html, string command, string value)
    {
        var (document, host) = Load(html);
        var before = host.InnerHtml;
        var inputEvents = 0;
        host.AddEventListener("input", _ => inputEvents++);

        Assert.False(document.QueryCommandEnabled(command));
        Assert.False(document.ExecCommand(command, showUI: false, value));
        Assert.Equal(before, host.InnerHtml);
        Assert.Equal(0, inputEvents);
    }

    [Theory]
    [InlineData("a[b]c", "delete", "", "ac")]
    [InlineData("a[b]c", "forwardDelete", "", "ac")]
    [InlineData("a[b]c", "insertText", "x", "axc")]
    [InlineData("<p>a[]c</p>", "insertParagraph", "", "<p>a</p><p>c</p>")]
    [InlineData("a[]c", "insertLineBreak", "", "a<br>c")]
    public void TextCommands_AreEnabled(string html, string command, string value, string expected)
    {
        var (document, host) = Load(html);
        Assert.True(document.QueryCommandEnabled(command));
        Assert.True(document.ExecCommand(command, showUI: false, value));
        Assert.Equal(expected, host.InnerHtml);
    }

    [Theory]
    // The outermost editing host of the editable content decides
    [InlineData("true", "<div contenteditable=\"plaintext-only\">[abc]</div>", "<div contenteditable=\"plaintext-only\"><b>abc</b></div>")]
    [InlineData("plaintext-only", "<div contenteditable=\"true\">[abc]</div>", "<div contenteditable=\"true\">abc</div>")]
    [InlineData("plaintext-only", "AB<div contenteditable=\"true\">C[D</div>E]F", "AB<div contenteditable=\"true\">CD</div>EF")]
    // An element that is not editable starts new editable content
    [InlineData("true", "<div contenteditable=\"false\"><div contenteditable=\"plaintext-only\">[abc]</div></div>", "<div contenteditable=\"false\"><div contenteditable=\"plaintext-only\">abc</div></div>")]
    [InlineData("plaintext-only", "<div contenteditable=\"false\"><div contenteditable=\"true\">[abc]</div></div>", "<div contenteditable=\"false\"><div contenteditable=\"true\"><b>abc</b></div></div>")]
    public void NestedEditingHosts_TheOutermostHostDecides(string outer, string html, string expected)
    {
        var (document, host) = Load(html, outer);
        document.ExecCommand("bold");
        Assert.Equal(expected, host.InnerHtml);
    }

    [Fact]
    public void DesignMode_IsNotPlainText()
    {
        var (document, host) = Load("[abc]");
        document.DesignMode = "on";
        document.GetSelection()!.SelectAllChildren(host);
        Assert.True(document.ExecCommand("bold"));
        Assert.Equal("<b>abc</b>", host.InnerHtml);
    }

    [Theory]
    [InlineData("normal", "", "plaintext", "plaintext")]
    [InlineData("normal", "", "<b>bold</b> and <i>italic</i>", "bold and italic")]
    [InlineData("normal", "", "1st line\n2nd line", "1st line 2nd line")]
    [InlineData("pre", "", "1st line\n2nd line", "1st line 2nd line")]
    [InlineData("normal", "", "<pre>1st line\n2nd line</pre>", "1st line<br>2nd line")]
    [InlineData("pre-wrap", "", "<pre>1st line\n2nd line</pre>", "1st line\n2nd line")]
    [InlineData("normal", "", "<div>abc</div><div>def</div>", "abc<br>def")]
    [InlineData("pre", "", "<div>abc<br>def</div><div>ghi</div>", "abc\ndef\nghi")]
    [InlineData("normal", "", "<script>script</script><style>style</style><noframes>frames</noframes>", "")]
    [InlineData("normal", "", "<html><head><title>title</title></head><body>body</body></html>", "body")]
    [InlineData("pre", "", "<span>plain  text</span>", "plain text")]
    [InlineData("pre", "", "<pre>plain  text</pre>", "plain  text")]
    [InlineData("normal", "<b>A[]B</b>", "<i>plain</i>text", "<b>AplaintextB</b>")]
    [InlineData("normal", "A[B]C", "", "AC")]
    public void InsertHtml_InsertsTheRenderedText(string whiteSpace, string html, string value, string expected)
    {
        var (document, host) = Load(html.Length == 0 ? "[]" : html, whiteSpace: whiteSpace);
        Assert.True(document.ExecCommand("insertHTML", showUI: false, value));
        Assert.Equal(expected, host.InnerHtml);
        Assert.Null(document.QuerySelector("#host ~ div"));
    }

    [Fact]
    public void InsertText_DoesNotCreateLinks()
    {
        var (document, host) = Load("http://example.com[]");
        Assert.True(document.ExecCommand("insertText", showUI: false, " "));
        Assert.Null(host.QuerySelector("a"));

        // No browser implements the autolinking of the specification
        (document, host) = Load("http://example.com[]", contentEditable: "true");
        Assert.True(document.ExecCommand("insertText", showUI: false, " "));
        Assert.Null(host.QuerySelector("a"));
    }

    [Theory]
    [InlineData("plaintext-only", "<p>a[b]c</p>", "insertLineBreak", "<p>a<br>c</p>")]
    [InlineData("true", "<p>a[b]c</p>", "insertParagraph", "<p>a</p><p>c</p>")]
    [InlineData("true", "<div contenteditable=\"plaintext-only\"><p>a[b]c</p></div>", "insertParagraph", "<div contenteditable=\"plaintext-only\"><p>a</p><p>c</p></div>")]
    [InlineData("plaintext-only", "<div contenteditable=\"true\"><p>a[b]c</p></div>", "insertLineBreak", "<div contenteditable=\"true\"><p>a<br>c</p></div>")]
    public void EnterKey_InsertsALineBreakInPlainText(string contentEditable, string html, string expectedInputType, string expected)
    {
        var (document, host) = Load(html, contentEditable);
        var inputTypes = new List<string>();
        host.AddEventListener("beforeinput", e => inputTypes.Add(((InputEvent)e).InputType));
        host.AddEventListener("input", e => inputTypes.Add(((InputEvent)e).InputType));

        Editing.UserEdit(document.GetSelection()!.AnchorNode!.ParentElement!, "insertParagraph", data: null);
        Assert.Equal(expected, host.InnerHtml);
        Assert.Equal([expectedInputType, expectedInputType], inputTypes);
    }

    [Theory]
    [InlineData("normal", "<p>AB[]</p>", "<p>AB<br><br></p>", "<p>AB<br>a</p>")]
    [InlineData("pre-wrap", "<p>AB[]</p>", "<p>AB\n<br></p>", "<p>AB\na</p>")]
    [InlineData("pre-wrap", "<p>A[]B</p>", "<p>A\nB</p>", "<p>A\naB</p>")]
    [InlineData("pre-line", "<p><b>AB[]</b></p>", "<p><b>AB\n<br></b></p>", "<p><b>AB\na</b></p>")]
    public void InsertLineBreak_ThePaddingLineBreakIsRemovedByTheNextText(string whiteSpace, string html, string afterLineBreak, string afterText)
    {
        var (document, host) = Load(html, whiteSpace: whiteSpace);
        Assert.True(document.ExecCommand("insertLineBreak"));
        Assert.Equal(afterLineBreak, host.InnerHtml);
        Assert.True(document.ExecCommand("insertText", showUI: false, "a"));
        Assert.Equal(afterText, host.InnerHtml);
    }

    [Fact]
    public void InsertText_RemovesTheLineFeedThatIsTheOnlyContent()
    {
        var (document, host) = Load("[]\n", whiteSpace: "pre-wrap");
        Assert.True(document.ExecCommand("insertText", showUI: false, "a"));
        Assert.Equal("a", host.InnerHtml);

        // A line feed that follows text is kept
        (document, host) = Load("a[]\n", whiteSpace: "pre-wrap");
        Assert.True(document.ExecCommand("insertText", showUI: false, "b"));
        Assert.Equal("ab\n", host.InnerHtml);
    }

    [Theory]
    // The last line stays visible when its content is deleted
    [InlineData("a\nb\nc[]", "delete", "a\nb\n<br>")]
    [InlineData("a\nb\n[]c", "forwardDelete", "a\nb\n<br>")]
    // The deletion of a line feed removes the line
    [InlineData("a\nb\n\n[]", "delete", "a\nb\n")]
    [InlineData("a\nb\n[]\n", "forwardDelete", "a\nb\n")]
    public void Delete_TheEmptyLastLineStaysVisible(string html, string command, string expected)
    {
        var (document, host) = Load(html, whiteSpace: "pre");
        Assert.True(document.ExecCommand(command));
        Assert.Equal(expected, host.InnerHtml);
    }

    [Theory]
    [InlineData("<p><b>[]AB</b></p>", "<p><b><br></b></p><p><b>AB</b></p>", "<p><b><br></b></p><p><b>aAB</b></p>")]
    [InlineData("<p><b>A[]B</b></p>", "<p><b>A</b></p><p><b>B</b></p>", "<p><b>A</b></p><p><b>aB</b></p>")]
    [InlineData("<p><b>AB[]</b></p>", "<p><b>AB</b></p><p><b><br></b></p>", "<p><b>AB</b></p><p><b>a</b></p>")]
    [InlineData("<p><i><b>AB[]</b></i></p>", "<p><i><b>AB</b></i></p><p><i><b><br></b></i></p>", "<p><i><b>AB</b></i></p><p><i><b>a</b></i></p>")]
    // The links do not continue in the new paragraph; the other inline elements of the caret are cloned
    [InlineData("<p><a href=\"/\">AB[]</a></p>", "<p><a href=\"/\">AB</a></p><p><br></p>", "<p><a href=\"/\">AB</a></p><p>a</p>")]
    [InlineData("<p><span>AB[]</span></p>", "<p><span>AB</span></p><p><span><br></span></p>", "<p><span>AB</span></p><p><span>a</span></p>")]
    [InlineData("<p><span style=\"color: red;\">AB[]</span></p>", "<p><span style=\"color: red;\">AB</span></p><p><span style=\"color: red;\"><br></span></p>", "<p><span style=\"color: red;\">AB</span></p><p><span style=\"color: red;\">a</span></p>")]
    public void InsertParagraph_KeepsTheInlineFormatting(string html, string afterParagraph, string afterText)
    {
        foreach (var contentEditable in new[] { "true", "plaintext-only" })
        {
            var (document, host) = Load(html, contentEditable);
            Assert.True(document.ExecCommand("insertParagraph"));
            Assert.Equal(afterParagraph, host.InnerHtml);
            Assert.True(document.ExecCommand("insertText", showUI: false, "a"));
            Assert.Equal(afterText, host.InnerHtml);
        }
    }

    [Theory]
    [InlineData("normal", "A[]B", "abc", "<b>abc</b>", "AabcB")]
    [InlineData("normal", "<b>A[]B</b>", "abc", "<i>abc</i>", "<b>AabcB</b>")]
    [InlineData("normal", "A[]B", "abc\ndef", "<div>abc</div><div>def</div>", "Aabc<br>defB")]
    [InlineData("pre", "A[]B", "abc\r\ndef", "<div>abc</div><div>def</div>", "Aabc\ndefB")]
    [InlineData("pre-wrap", "<b>A[]B</b>", "abc\ndef", "<div>abc</div><div>def</div>", "<b>Aabc\ndefB</b>")]
    // Without plain text on the clipboard, the rendered text of the HTML is pasted
    [InlineData("normal", "A[]B", null, "<div>abc</div><div>d<b>e</b>f</div>", "Aabc<br>defB")]
    public void Paste_InPlainText_PastesTheText(string whiteSpace, string html, string? text, string clipboardHtml, string expected)
    {
        var (document, host) = Load(html, whiteSpace: whiteSpace);
        WriteClipboard(text, clipboardHtml);
        var events = new List<string>();
        foreach (var type in new[] { "beforeinput", "input" })
        {
            host.AddEventListener(type, e =>
            {
                var input = (InputEvent)e;
                events.Add($"{type} {input.InputType} {input.Data ?? "null"} {input.DataTransfer?.GetData("text/html")}");
            });
        }

        Assert.True(ClipboardActions.Paste(document, scriptTriggered: false));
        Assert.Equal(expected, host.InnerHtml);
        Assert.Equal([$"beforeinput insertFromPaste null {clipboardHtml}", $"input insertFromPaste null {clipboardHtml}"], events);
    }

    [Theory]
    [InlineData("A[]B", "<b>abc</b>", "A<b>abc</b>B")]
    [InlineData("[]", "<a href=\"https://example.org/\">link</a>", "<a href=\"https://example.org/\">link</a>")]
    [InlineData("A[BC]D", "<i>x</i>", "A<i>x</i>D")]
    // The Text nodes of the boundaries are merged with their siblings
    [InlineData("foo []bar", "Select", "foo Selectbar")]
    // The content that runs scripts, the comments and the elements of the head are removed
    [InlineData("A[]B", "<meta charset=\"utf-8\"><style>b { color: red }</style><!--StartFragment--><b onclick=\"alert(1)\">x</b><script>alert(2)</script><a href=\"javascript:alert(3)\">y</a><!--EndFragment-->", "A<b>x</b><a>y</a>B")]
    public void Paste_InRichText_PastesTheHtml(string html, string clipboardHtml, string expected)
    {
        var (document, host) = Load(html, contentEditable: "true");
        WriteClipboard("ignored", clipboardHtml);
        Assert.True(ClipboardActions.Paste(document, scriptTriggered: false));
        Assert.Equal(expected, host.InnerHtml);
    }

    [Fact]
    public void Paste_InRichText_MergesTheTextNodes()
    {
        var (document, host) = Load("foo []bar", contentEditable: "true");
        WriteClipboard("Select", "Select");
        Assert.True(ClipboardActions.Paste(document, scriptTriggered: false));
        Assert.Equal("foo Selectbar", Assert.IsType<Text>(Assert.Single(host.ChildNodes), exactMatch: false).Data);

        // The caret is after the pasted text
        var selection = document.GetSelection()!;
        Assert.True(selection.IsCollapsed);
        Assert.Same(host.FirstChild, selection.AnchorNode);
        Assert.Equal(10u, selection.AnchorOffset);
    }

    [Fact]
    public void Paste_InRichText_ASpaceAtTheEndOfTheLineIsKeptVisible()
    {
        var (document, host) = LoadDocument("<span id=host contenteditable>ABC DEF GHI[]</span>");
        WriteClipboard("DEF ", "DEF ");
        Assert.True(ClipboardActions.Paste(document, scriptTriggered: false));
        Assert.Equal("ABC DEF GHIDEF&nbsp;", host.InnerHtml);
    }

    [Theory]
    [InlineData("normal", "<p>[]<br></p>", "abc\ndef\nghi", "<p>abc<br>def<br>ghi</p>", 5)]
    [InlineData("pre-wrap", "<p>[]<br></p>", "abc\ndef\nghi", "<p>abc\ndef\nghi</p>", 1)]
    [InlineData("pre", "<p>x[]</p>", "abc\n", "<p>xabc\n<br></p>", 2)]
    public void Paste_PlainTextWithLineBreaks(string whiteSpace, string html, string text, string expected, int expectedChildCount)
    {
        var (document, host) = Load(html, contentEditable: "true", whiteSpace: whiteSpace);
        WriteClipboard(text, html: null);
        var types = new List<string>();
        host.AddEventListener("beforeinput", e => types.AddRange(((InputEvent)e).DataTransfer!.Types));

        Assert.True(ClipboardActions.Paste(document, scriptTriggered: false));
        Assert.Equal(expected, host.InnerHtml);
        Assert.Equal(expectedChildCount, host.FirstChild!.ChildNodes.Count);
        Assert.Equal(["text/plain"], types);
    }

    [Fact]
    public void Paste_PlainText_DoesNotCreateLinks()
    {
        var (document, host) = Load("[]", contentEditable: "true");
        WriteClipboard("see http://example.com now", html: null);
        Assert.True(ClipboardActions.Paste(document, scriptTriggered: false));
        Assert.Equal("see http://example.com now", Normalize(host.InnerHtml));
    }

    [Fact]
    public void PasteAsPlainText_OnlyThePlainTextIsAvailable()
    {
        var (document, host) = Load("A[]B", contentEditable: "true");
        WriteClipboard("abc", "<b>abc</b>");
        var types = new List<string>();
        host.AddEventListener("paste", e => types.Add("paste:" + string.Join("|", ((ClipboardEvent)e).ClipboardData!.Types)));
        host.AddEventListener("beforeinput", e => types.Add("beforeinput:" + string.Join("|", ((InputEvent)e).DataTransfer!.Types)));

        Assert.True(ClipboardActions.Paste(document, scriptTriggered: false, asPlainText: true));
        Assert.Equal("AabcB", host.InnerHtml);
        Assert.Equal(["paste:text/plain", "beforeinput:text/plain"], types);
    }

    [Fact]
    public void CutAndPaste_FireTheInputEvents()
    {
        var (document, host) = LoadDocument("<p id=host contenteditable><b>rich</b></p>");
        document.GetSelection()!.SelectAllChildren(host);
        var events = new List<string>();
        foreach (var type in new[] { "cut", "paste", "beforeinput", "input" })
        {
            host.AddEventListener(type, e => events.Add(e is InputEvent input
                ? $"{type} {input.InputType} {input.Data ?? "null"} {input.DataTransfer?.GetData("text/plain")}|{input.DataTransfer?.GetData("text/html")}"
                : type));
        }

        Assert.True(ClipboardActions.Cut(document, scriptTriggered: false));
        Assert.Equal("<br>", host.InnerHtml);
        Assert.True(ClipboardActions.Paste(document, scriptTriggered: false));
        Assert.Equal("<b>rich</b>", host.InnerHtml);
        Assert.Equal(
            [
                "cut", "beforeinput deleteByCut null |", "input deleteByCut null |",
                "paste", "beforeinput insertFromPaste null rich|<b>rich</b>", "input insertFromPaste null rich|<b>rich</b>",
            ],
            events);
    }

    [Fact]
    public void Paste_CanceledBeforeInput_DoesNotChangeTheContent()
    {
        var (document, host) = Load("A[]B", contentEditable: "true");
        WriteClipboard("abc", "<b>abc</b>");
        host.AddEventListener("beforeinput", e => e.PreventDefault());
        ClipboardActions.Paste(document, scriptTriggered: false);
        Assert.Equal("AB", host.InnerHtml);
    }

    [Theory]
    [InlineData("ABC [DEF ]GHI", "DEF ")]
    [InlineData("[ABC ]<b>DEF</b>", "ABC ")]
    [InlineData("ABC [DEF ]", "DEF")]
    [InlineData("ABC [DEF ]<br>GHI", "DEF")]
    public void Copy_ASpaceThatIsRenderedAtTheEndOfTheSelectionIsCopied(string html, string expected)
    {
        var (document, _) = Load(html, contentEditable: "true");
        Assert.True(document.ExecCommand("copy"));
        Assert.Equal(expected, _clipboard.ReadText());
        Assert.Equal(expected, document.GetSelection()!.ToString());
    }

    [Fact]
    public void Copy_TheBlocksOfAnAtomicInlineDoNotBreakTheLine()
    {
        var (document, host) = LoadDocument("<div id=host><span>test<div style=\"display: inline-flex\"><button> </button></div>;</span></div>");
        document.GetSelection()!.SelectAllChildren(host);
        Assert.True(document.ExecCommand("copy"));
        Assert.Equal("test;", _clipboard.ReadText());
    }
}
