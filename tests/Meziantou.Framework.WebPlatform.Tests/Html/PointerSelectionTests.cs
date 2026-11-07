using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Tests.Html;

/// <summary>The selection with the mouse: the caret of a press, the selection of a drag, of a double click and of a triple click.</summary>
public sealed class PointerSelectionTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();

    public void Dispose() => _contexts.Dispose();

    private Document Load(string html)
    {
        var document = _contexts.Create().Navigate("https://example.com/", _ => "<!DOCTYPE html><style>body { margin: 0; font: 20px monospace } p { margin: 0 }</style>" + html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    /// <summary>The point of the viewport between two characters of the text of an element.</summary>
    private static (double X, double Y) GetPoint(Document document, string id, uint offset)
    {
        var text = document.GetElementById(id)!.FirstChild!;
        var range = document.CreateRange();
        range.SetStart(text, offset);
        range.SetEnd(text, offset);
        var rect = range.GetBoundingClientRect();
        return (rect.Left, rect.Top + (rect.Height / 2));
    }

    [Fact]
    public void Press_PutsTheCaretAtThePosition_AndDragExtendsTheSelection()
    {
        var document = Load("<p id=p>hello world</p><p id=q>second line</p>");
        var mouse = document.BrowsingContext!.Input.Mouse;
        var selection = document.GetSelection()!;
        var text = document.GetElementById("p")!.FirstChild;

        var (x, y) = GetPoint(document, "p", 2);
        mouse.Move(x + 1, y);
        mouse.Down();
        Assert.Same(text, selection.AnchorNode);
        Assert.Equal(2u, selection.AnchorOffset);
        Assert.True(selection.IsCollapsed);

        var (x2, y2) = GetPoint(document, "q", 6);
        mouse.Move(x2 + 1, y2);
        Assert.Same(text, selection.AnchorNode);
        Assert.Equal(2u, selection.AnchorOffset);
        Assert.Same(document.GetElementById("q")!.FirstChild, selection.FocusNode);
        Assert.Equal(6u, selection.FocusOffset);
        Assert.Equal("forward", selection.Direction);
        mouse.Up();

        // The Shift key extends the selection from its anchor
        var (x3, y3) = GetPoint(document, "p", 0);
        document.BrowsingContext.Input.Modifiers = InputModifiers.Shift;
        mouse.Move(x3, y3);
        mouse.Down();
        mouse.Up();
        document.BrowsingContext.Input.Modifiers = InputModifiers.None;
        Assert.Equal("he", selection.ToString());
        Assert.Equal("backward", selection.Direction);
    }

    [Fact]
    public void DoubleClick_SelectsTheWord_TripleClickSelectsTheBlock()
    {
        var document = Load("<p id=p>hello world</p>");
        var mouse = document.BrowsingContext!.Input.Mouse;
        var selection = document.GetSelection()!;
        var (x, y) = GetPoint(document, "p", 8);
        mouse.Move(x + 1, y);
        mouse.Down();
        mouse.Up();
        mouse.Down();
        mouse.Up();
        Assert.Equal("world", selection.ToString());
        Assert.Equal("none", selection.Direction);

        mouse.Down();
        mouse.Up();
        Assert.Same(document.GetElementById("p"), selection.AnchorNode);
        Assert.Equal("hello world", selection.ToString());
    }

    [Fact]
    public void Press_CanceledSelectStartOrUserSelectNone_KeepsTheSelection()
    {
        var document = Load("<p id=p>hello world</p><p id=q style='user-select: none'>second line</p>");
        var mouse = document.BrowsingContext!.Input.Mouse;
        var selection = document.GetSelection()!;
        var text = document.GetElementById("p")!.FirstChild!;
        selection.Collapse(text, 1);

        var (x, y) = GetPoint(document, "q", 3);
        mouse.Move(x + 1, y);
        mouse.Down();
        mouse.Up();
        Assert.Same(text, selection.AnchorNode);
        Assert.Equal(1u, selection.AnchorOffset);

        var count = 0;
        document.AddEventListener("selectstart", e =>
        {
            count++;
            e.PreventDefault();
        });
        (x, y) = GetPoint(document, "p", 4);
        mouse.Move(x + 1, y);
        mouse.Down();
        mouse.Up();
        Assert.Equal(1, count);
        Assert.Equal(1u, selection.AnchorOffset);
    }

    [Fact]
    public void Press_InATextControl_SetsItsSelection()
    {
        var document = Load("<input id=input value='hello world' style='font: 20px monospace; padding: 0; border: 0; width: 400px'>");
        var mouse = document.BrowsingContext!.Input.Mouse;
        var input = (HtmlInputElement)document.GetElementById("input")!;
        var rect = input.GetBoundingClientRect();
        var caret = document.CaretPositionFromPoint(rect.Left + 30, rect.Top + (rect.Height / 2))!;
        Assert.Same(input, caret.OffsetNode);

        mouse.Move(rect.Left + 30, rect.Top + (rect.Height / 2));
        mouse.Down();
        Assert.Same(input, document.ActiveElement);
        Assert.Equal(caret.Offset, input.SelectionStart);
        Assert.Equal(caret.Offset, input.SelectionEnd);

        mouse.Move(rect.Left + 390, rect.Top + (rect.Height / 2));
        mouse.Up();
        Assert.Equal(caret.Offset, input.SelectionStart);
        Assert.Equal(11u, input.SelectionEnd);
    }

    [Fact]
    public void DoubleClick_InATextControl_SelectsAWordOfItsValue()
    {
        var document = Load("<input id=input value='hello world' style='font: 20px monospace; padding: 0; border: 0; width: 400px'>");
        var mouse = document.BrowsingContext!.Input.Mouse;
        var input = (HtmlInputElement)document.GetElementById("input")!;
        var rect = input.GetBoundingClientRect();
        mouse.Move(rect.Left + 30, rect.Top + (rect.Height / 2));
        mouse.Down();
        mouse.Up();
        mouse.Down();
        mouse.Up();
        Assert.Equal(0u, input.SelectionStart);
        Assert.Equal(5u, input.SelectionEnd);

        mouse.Down();
        mouse.Up();
        Assert.Equal(0u, input.SelectionStart);
        Assert.Equal(11u, input.SelectionEnd);
    }

    [Fact]
    public void Press_InAnEditingHost_PutsTheCaretAtThePositionInsteadOfItsStart()
    {
        var document = Load("<p id=p contenteditable>hello world</p>");
        var mouse = document.BrowsingContext!.Input.Mouse;
        var selection = document.GetSelection()!;
        var (x, y) = GetPoint(document, "p", 6);
        mouse.Move(x + 1, y);
        mouse.Down();
        mouse.Up();
        Assert.Same(document.GetElementById("p"), document.ActiveElement);
        Assert.Equal(6u, selection.AnchorOffset);
    }
}
