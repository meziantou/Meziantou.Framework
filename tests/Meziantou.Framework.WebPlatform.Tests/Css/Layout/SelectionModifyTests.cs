using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Layout;

/// <summary>
/// The caret movements of <c>Selection.modify()</c> over the layout (https://w3c.github.io/selection-api/#dom-selection-modify):
/// characters (logical and visual in bidi text), words, lines keeping the inline coordinate, boundaries, the editing hosts.
/// </summary>
public sealed class SelectionModifyTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();

    public void Dispose() => _contexts.Dispose();

    /// <summary>Parses a no-quirks document whose text uses the Ahem font (1em glyphs), in a 800x600 viewport.</summary>
    private Document Parse(string html)
    {
        var context = _contexts.Create(new BrowsingContextOptions { Viewport = new ViewportOptions { Width = 800, Height = 600 } });
        var document = context.Navigate("https://example.com/", _ => "<!DOCTYPE html><style>body { margin: 0; font: 10px/10px monospace }</style>" + html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    private static Node TextOf(Document document, string id, int index = 0) => document.GetElementById(id)!.ChildNodes[index];

    private static (Node Node, uint Offset) Modify(Selection selection, string alter, string direction, string granularity, int count = 1)
    {
        for (var i = 0; i < count; i++)
        {
            selection.Modify(alter, direction, granularity);
        }

        return (selection.FocusNode!, selection.FocusOffset);
    }

    [Fact]
    public void Character_ForwardAndBackward()
    {
        var document = Parse("<p id=p>Hello World</p>");
        var text = TextOf(document, "p");
        var selection = document.GetSelection()!;
        selection.Collapse(text, 3);
        Assert.Equal((text, 5u), Modify(selection, "move", "forward", "character", 2));
        Assert.Equal((text, 2u), Modify(selection, "move", "backward", "character", 3));
    }

    [Fact]
    public void Character_StaysInTheTextNodeOfTheCrossedCharacter()
    {
        var document = Parse("<p id=p><b id=b>abc</b><i id=i>def</i></p>");
        var abc = TextOf(document, "b");
        var def = TextOf(document, "i");
        var selection = document.GetSelection()!;
        selection.Collapse(abc, 2);
        Assert.Equal((abc, 3u), Modify(selection, "move", "right", "character"));
        Assert.Equal((def, 1u), Modify(selection, "move", "right", "character"));
        Assert.Equal((def, 0u), Modify(selection, "move", "left", "character"));
        Assert.Equal((abc, 2u), Modify(selection, "move", "left", "character"));
    }

    [Fact]
    public void Character_SkipsCollapsedWhiteSpace()
    {
        var document = Parse("<p id=p>abc   <span id=s>  def</span></p>");
        var abc = TextOf(document, "p");
        var def = TextOf(document, "s");
        var selection = document.GetSelection()!;
        selection.Collapse(abc, 3);
        Assert.Equal((abc, 4u), Modify(selection, "move", "forward", "character"));
        Assert.Equal((def, 3u), Modify(selection, "move", "forward", "character"));
    }

    [Fact]
    public void Character_LineBreaks()
    {
        var document = Parse("<pre id=a>foo\nbar</pre><pre id=b>foo<br>bar</pre><pre id=c>\nfoo\n<br>\nbar\n</pre>");
        var selection = document.GetSelection()!;
        var a = TextOf(document, "a");
        selection.Collapse(a, 3);
        Assert.Equal((a, 4u), Modify(selection, "move", "forward", "character"));
        Assert.Equal((a, 3u), Modify(selection, "move", "backward", "character"));

        var b = document.GetElementById("b")!;
        selection.Collapse(b.ChildNodes[0], 3);
        Assert.Equal((b.ChildNodes[2], 0u), Modify(selection, "move", "forward", "character"));
        Assert.Equal((b.ChildNodes[0], 3u), Modify(selection, "move", "backward", "character"));

        var c = document.GetElementById("c")!;
        selection.Collapse(c, 1);
        Assert.Equal((c.ChildNodes[2], 0u), Modify(selection, "move", "forward", "character"));
        selection.Collapse(c, 2);
        Assert.Equal((c.ChildNodes[0], 4u), Modify(selection, "move", "backward", "character"));
    }

    [Theory]
    [InlineData("right", 4, 3, 11)]
    [InlineData("left", 11, 3, 4)]
    [InlineData("right", 7, 3, 14)]
    [InlineData("left", 14, 3, 7)]
    [InlineData("forward", 4, 3, 7)]
    [InlineData("backward", 11, 3, 8)]
    public void Character_BidiVisualMovement(string direction, int start, int count, int expected)
    {
        var document = Parse("<div id=d dir=ltr>Hello אבגדהו World</div>");
        var text = TextOf(document, "d");
        var selection = document.GetSelection()!;
        selection.Collapse(text, (uint)start);
        Assert.Equal((text, (uint)expected), Modify(selection, "move", direction, "character", count));
    }

    [Theory]
    [InlineData("right", 17, 3, 5)]
    [InlineData("left", 5, 3, 17)]
    public void Character_BidiVisualMovement_RightToLeftParagraph(string direction, int start, int count, int expected)
    {
        var document = Parse("<div id=d dir=auto>אבגדהו Hello World</div>");
        var text = TextOf(document, "d");
        var selection = document.GetSelection()!;
        selection.Collapse(text, (uint)start);
        Assert.Equal((text, (uint)expected), Modify(selection, "extend", direction, "character", count));
        Assert.Equal((text, (uint)start), (selection.AnchorNode, selection.AnchorOffset));
    }

    [Fact]
    public void Word_EndOfWordForwardAndStartOfWordBackward()
    {
        var document = Parse("<div id=d>This is a line<span style='display:inline-block'></span></div><div>Test, these are <strong id=s> strong </strong> normal.</div>");
        var text = TextOf(document, "d");
        var selection = document.GetSelection()!;
        selection.Collapse(text, 0);
        Modify(selection, "extend", "forward", "word", 4);
        Assert.Equal("This is a line", selection.GetRangeAt(0).ToString());

        var strong = TextOf(document, "s");
        selection.Collapse(strong, 4);
        Assert.Equal((strong, 1u), Modify(selection, "extend", "backward", "word"));
    }

    [Theory]
    [InlineData("~>>>>>p+++", 6, "backward", 0)]
    [InlineData("~>>>>>p+++", 7, "forward", 10)]
    [InlineData("희진DJ", 0, "forward", 2)]
    [InlineData("희진DJ", 4, "backward", 2)]
    public void Word_Segments(string content, int start, string direction, int expected)
    {
        var document = Parse("<div id=d contenteditable>" + content + "</div>");
        var text = TextOf(document, "d");
        var selection = document.GetSelection()!;
        selection.Collapse(text, (uint)start);
        Assert.Equal((text, (uint)expected), Modify(selection, "move", direction, "word"));
    }

    [Fact]
    public void Line_KeepsTheInlineCoordinate()
    {
        var document = Parse("<div id=c><p id=one>One</p><p id=two>Two</p><p id=three>Three</p></div>");
        var selection = document.GetSelection()!;
        selection.Collapse(TextOf(document, "one"), 2);
        Assert.Equal((TextOf(document, "two"), 2u), Modify(selection, "extend", "forward", "line"));
        Assert.Equal((TextOf(document, "three"), 2u), Modify(selection, "extend", "forward", "line"));
        Assert.Equal((TextOf(document, "one"), 2u), (selection.AnchorNode, selection.AnchorOffset));
    }

    [Fact]
    public void Line_KeepsTheCoordinateAcrossAShortLine()
    {
        var document = Parse("<div id=d>abcdef<br>ab<br>abcdef</div>");
        var div = document.GetElementById("d")!;
        var selection = document.GetSelection()!;
        selection.Collapse(div.ChildNodes[0], 5);
        Assert.Equal((div.ChildNodes[2], 2u), Modify(selection, "move", "forward", "line"));
        Assert.Equal((div.ChildNodes[4], 5u), Modify(selection, "move", "forward", "line"));
    }

    [Fact]
    public void Line_VerticalWritingMode()
    {
        var document = Parse("<div id=d style='writing-mode: vertical-lr; height: 200px'>text1<br>text2</div>");
        var div = document.GetElementById("d")!;
        var selection = document.GetSelection()!;
        selection.Collapse(div.ChildNodes[0], 3);
        Assert.Equal((div.ChildNodes[2], 3u), Modify(selection, "move", "forward", "line"));
        Assert.Equal((div.ChildNodes[0], 3u), Modify(selection, "move", "backward", "line"));
    }

    [Fact]
    public void LineBoundary_StaysInTheInlineEditingHost()
    {
        var document = Parse("<div>Prefix: <span id=h contenteditable>Edit<br>able</span>: Suffix</div>");
        var host = document.GetElementById("h")!;
        var selection = document.GetSelection()!;
        selection.Collapse(host.ChildNodes[0], 1);
        Assert.Equal((host.ChildNodes[0], 4u), Modify(selection, "move", "forward", "lineboundary"));
        selection.Collapse(host.ChildNodes[2], 1);
        Assert.Equal((host.ChildNodes[2], 4u), Modify(selection, "move", "forward", "lineboundary"));
        Assert.Equal((host.ChildNodes[2], 0u), Modify(selection, "move", "backward", "lineboundary"));
    }

    [Theory]
    [InlineData("ltr", "left", 0)]
    [InlineData("ltr", "right", 10)]
    [InlineData("rtl", "left", 10)]
    [InlineData("rtl", "right", 0)]
    public void LineBoundary_LeftAndRightFollowTheDirectionOfTheLine(string dir, string direction, int expected)
    {
        var document = Parse($"<div id=d dir={dir}>مرحبا عالم</div>");
        var div = document.GetElementById("d")!;
        var selection = document.GetSelection()!;
        selection.Collapse(div, 0);
        Modify(selection, "extend", direction, "lineboundary");
        Assert.Equal((uint)expected, selection.FocusOffset);
    }

    [Fact]
    public void NonEditableElementsOfAnEditingHostAreAtomic()
    {
        var document = Parse("<div id=h contenteditable>this <a contenteditable=false>link</a> after</div>");
        var host = document.GetElementById("h")!;
        var selection = document.GetSelection()!;
        selection.Collapse(host.ChildNodes[0], 5);
        Assert.Equal((host.ChildNodes[2], 0u), Modify(selection, "move", "forward", "character"));
        selection.Collapse(host.ChildNodes[2], 6);
        Assert.Equal((host.ChildNodes[0], 0u), Modify(selection, "move", "backward", "paragraphboundary"));
        selection.Collapse(host, 0);
        Modify(selection, "extend", "forward", "word", 5);
        Assert.Same(host, selection.FocusNode!.ParentNode);
    }

    [Fact]
    public void Paragraph_MovesToThePreviousParagraphAtTheSameCoordinate()
    {
        var document = Parse("<div id=h contenteditable style='width: 100px'><p id=p1>P1</p><p>Line 1 Line 2 Line 3 <span id=end>World</span></p></div>");
        var selection = document.GetSelection()!;
        selection.Collapse(TextOf(document, "end"), 5);
        Assert.Equal((TextOf(document, "p1"), 2u), Modify(selection, "move", "backward", "paragraph"));
    }

    [Fact]
    public void Move_WithARange_CollapsesByCharacter()
    {
        var document = Parse("<p id=p>Hello World</p>");
        var text = TextOf(document, "p");
        var selection = document.GetSelection()!;
        selection.SetBaseAndExtent(text, 2, text, 6);
        Assert.Equal((text, 6u), Modify(selection, "move", "forward", "character"));
        selection.SetBaseAndExtent(text, 2, text, 6);
        Assert.Equal((text, 2u), Modify(selection, "move", "backward", "character"));
    }

    [Fact]
    public void DocumentBoundary()
    {
        var document = Parse("<p id=a>abc</p><p id=b>def</p>");
        var selection = document.GetSelection()!;
        selection.Collapse(TextOf(document, "a"), 1);
        Assert.Equal((TextOf(document, "b"), 3u), Modify(selection, "extend", "forward", "documentboundary"));
        Assert.Equal((TextOf(document, "a"), 0u), Modify(selection, "move", "backward", "documentboundary"));
    }

    private static void PressKey(Node target, string key, bool shift = false, bool control = false)
    {
        var keyboardEvent = new KeyboardEvent("keydown", new KeyboardEventInit { Key = key, ShiftKey = shift, CtrlKey = control, Bubbles = true, Cancelable = true }) { IsTrusted = true };
        EventDispatcher.Dispatch(keyboardEvent, target);
    }

    [Fact]
    public void Keys_MoveTheCaretInEditableContent()
    {
        var document = Parse("<div id=h contenteditable>Hello world<br>Second line</div>");
        var host = (HtmlElement)document.GetElementById("h")!;
        host.Focus();
        var selection = document.GetSelection()!;

        // Focusing the editing host placed the caret at its start
        Assert.Equal((host.ChildNodes[0], 0u), (selection.FocusNode!, selection.FocusOffset));
        PressKey(host, "ArrowRight");
        Assert.Equal((host.ChildNodes[0], 1u), (selection.FocusNode!, selection.FocusOffset));
        PressKey(host, "ArrowRight", control: true);
        Assert.Equal((host.ChildNodes[0], 5u), (selection.FocusNode!, selection.FocusOffset));
        PressKey(host, "End", shift: true);
        Assert.Equal(" world", selection.ToString());
        PressKey(host, "ArrowDown");
        Assert.Equal((host.ChildNodes[2], 11u), (selection.FocusNode!, selection.FocusOffset));
        PressKey(host, "Home", control: true);
        Assert.Equal((host.ChildNodes[0], 0u), (selection.FocusNode!, selection.FocusOffset));
    }

    [Fact]
    public void Keys_MoveTheCaretInTextControls()
    {
        var document = Parse("<input id=i value='foo bar+baz'><textarea id=t>abc\ndefgh\nij</textarea>");
        var input = (HtmlInputElement)document.GetElementById("i")!;
        input.Focus();
        input.SetSelectionRange(0, 0);
        PressKey(input, "ArrowRight", control: true);
        Assert.Equal(3u, input.SelectionStart);
        PressKey(input, "ArrowRight", shift: true);
        Assert.Equal((3u, 4u, "forward"), (input.SelectionStart!.Value, input.SelectionEnd!.Value, input.SelectionDirection));
        PressKey(input, "End");
        Assert.Equal((11u, 11u), (input.SelectionStart!.Value, input.SelectionEnd!.Value));
        PressKey(input, "ArrowLeft", control: true);
        Assert.Equal(8u, input.SelectionStart);

        var textArea = (HtmlTextAreaElement)document.GetElementById("t")!;
        textArea.Focus();
        textArea.SetSelectionRange(9, 9);
        PressKey(textArea, "ArrowUp");
        Assert.Equal(3u, textArea.SelectionStart);
        PressKey(textArea, "ArrowDown");
        PressKey(textArea, "ArrowDown");
        Assert.Equal(12u, textArea.SelectionStart);
        PressKey(textArea, "Home");
        Assert.Equal(10u, textArea.SelectionStart);
    }

    [Fact]
    public void InvalidArgumentsDoNothing()
    {
        var document = Parse("<p id=p>Hello</p>");
        var text = TextOf(document, "p");
        var selection = document.GetSelection()!;
        selection.Collapse(text, 1);
        selection.Modify("jump", "forward", "character");
        selection.Modify("move", "up", "character");
        selection.Modify("move", "forward", "letter");
        selection.Modify();
        Assert.Equal((text, 1u), (selection.FocusNode, selection.FocusOffset));
        selection.Modify("MOVE", "Forward", "CHARACTER");
        Assert.Equal((text, 2u), (selection.FocusNode, selection.FocusOffset));
    }
}
