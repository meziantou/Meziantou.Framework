using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Dom;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Layout;

/// <summary>
/// The inner editor of the text controls (https://html.spec.whatwg.org/multipage/rendering.html#the-textarea-element-2): the
/// value of a <c>textarea</c> or of a text field is laid out by the inline layout in an anonymous block of the replaced box of
/// the control, which gives its lines, its scrolling area, its selection highlight and its caret positions.
/// </summary>
public sealed class TextControlLayoutTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly FakeTimeProvider _clock = new();

    public void Dispose() => _contexts.Dispose();

    /// <summary>
    /// Parses a no-quirks document whose body has no margin, in a 800x600 viewport with overlay scrollbars; the text controls
    /// have the 10px/1 Ahem font, no border and no padding.
    /// </summary>
    private Document Parse(string html, double scrollbarThickness = 0)
    {
        var context = _contexts.Create(new BrowsingContextOptions { TimeProvider = _clock, Viewport = new ViewportOptions { Width = 800, Height = 600, ScrollbarThickness = scrollbarThickness } });
        var document = context.Navigate("https://example.com/", _ => "<!DOCTYPE html><style>body { margin: 0; font: 10px/1 Ahem } textarea, input { font: 10px/1 Ahem; border: 0; padding: 0; margin: 0; vertical-align: top }</style>" + html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    private static BoxFragment GetFragment(Document document, string id) => document.GetLayoutEngine().GetFragments(document.GetElementById(id)!)[0];

    /// <summary>Returns the texts of the lines of the inner editor of a text control.</summary>
    private static string[] GetLines(Document document, string id)
    {
        var editor = TextControlLayout.GetInnerEditorFragment(GetFragment(document, id))!;
        return [.. editor.Children.OfType<LineBoxFragment>().Select(line => string.Concat(line.Children.OfType<TextFragment>().Where(text => text.Run.Kind == TextRunKind.Generated).Select(text => text.Run.Data[text.Start..text.End])))];
    }

    private static List<string> GetDrawings(Document document)
        => [.. document.GetDisplayList()!.Dump().Split('\n').Select(line => line.Trim()).Where(line => line.StartsWith("draw-glyphs", StringComparison.Ordinal) || line.StartsWith("draw-rect", StringComparison.Ordinal))];

    private static (double X, double Y, double Width, double Height) ToTuple(DomRect rect) => (rect.X, rect.Y, rect.Width, rect.Height);

    [Fact]
    public void TextArea_ValueIsLaidOutByTheInlineLayout()
    {
        // The lines wrap in the content box (5 columns), the spaces at the end of the lines hang (pre-wrap)
        var document = Parse("<textarea id=t cols=5 rows=2>abc de fghi\nj</textarea>");
        Assert.Equal(["abc ", "de ", "fghi", "j"], GetLines(document, "t"));

        var fragment = GetFragment(document, "t");
        Assert.Equal(new PhysicalSize(50, 20), fragment.Size);
        var editor = TextControlLayout.GetInnerEditorFragment(fragment)!;
        Assert.Equal(new PhysicalRect(0, 0, 50, 40), new PhysicalRect(editor.Offset, editor.Size));

        // The baseline of the control is the one of the first line of its inner editor
        Assert.Equal(8, fragment.FirstBaseline);
    }

    [Theory]
    [InlineData("white-space: pre", "a  b c d e f|g")]
    [InlineData("white-space: nowrap", "a  b c d e f|g")]
    [InlineData("white-space: normal", "a  b |c d e |f|g")]
    [InlineData("white-space: break-spaces", "a  b |c d |e f|g")]
    [InlineData("overflow-wrap: normal", "a  b |c d e |f|g")]
    public void TextArea_WhiteSpace(string style, string expected)
    {
        // As in the browsers, a textarea always preserves the white space of its value: only the wrapping of white-space applies
        var document = Parse($"<textarea id=t cols=5 style='{style}'>a  b c d e f\ng</textarea>");
        Assert.Equal(expected, string.Join('|', GetLines(document, "t")));
    }

    [Fact]
    public void TextArea_WrapOffDoesNotWrap()
    {
        var document = Parse("<textarea id=t cols=5 wrap=OFF>a b c d e f</textarea>");
        Assert.Equal(["a b c d e f"], GetLines(document, "t"));
    }

    [Fact]
    public void TextArea_BreaksTheWordsThatOverflow()
    {
        // overflow-wrap: break-word in the user agent style sheet, as in the browsers
        var document = Parse("<textarea id=t cols=5>abcdefghijkl</textarea>");
        Assert.Equal("break-word", document.DefaultView!.GetComputedStyle(document.GetElementById("t")!).GetPropertyValue("overflow-wrap"));
        Assert.Equal(["abcde", "fghij", "kl"], GetLines(document, "t"));
    }

    [Fact]
    public void TextArea_IsTheScrollContainerOfItsInnerEditor()
    {
        var document = Parse("<textarea id=t cols=5 rows=2 style='padding: 3px'>a\nb\nc\nd\n</textarea>");
        var textArea = document.GetElementById("t")!;

        // 5 lines (the line after the last line feed is rendered) and the paddings
        Assert.Equal(56, textArea.ScrollHeight);
        Assert.Equal(26, textArea.ClientHeight);
        textArea.ScrollTop = 1000;
        Assert.Equal(30, textArea.ScrollTop);

        // The lines are painted at the scroll position, in the clip of the padding box
        var dump = document.GetDisplayList()!.Dump();
        Assert.Contains("clip-rect (0, 0, 56x26)", dump, StringComparison.Ordinal);
        Assert.Contains("draw-glyphs \"a\" Ahem 10px at (3, -19) glyphs=1", dump, StringComparison.Ordinal);
        Assert.Contains("draw-glyphs \"d\" Ahem 10px at (3, 11) glyphs=1", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void TextArea_ScrollbarTakesTheSpaceReservedInTheNaturalSize()
    {
        // cols x the average width + the thickness of the scrollbar: the lines wrap in the whole content box while the scrollbar
        // is not displayed, and in the 5 columns when it is
        var document = Parse("<textarea id=a cols=5 rows=2>abcdefg</textarea><textarea id=b cols=5 rows=2>abcdefg h i j k l m n o p</textarea><textarea id=c cols=5 rows=2 style='overflow-y: scroll'>abcdefg</textarea>", scrollbarThickness: 15);
        Assert.Equal(new PhysicalSize(65, 20), GetFragment(document, "a").Size);
        Assert.Equal(["abcdef", "g"], GetLines(document, "a"));
        Assert.Equal(new PhysicalSize(65, 20), GetFragment(document, "b").Size);
        Assert.Equal(50, GetFragment(document, "b").ContentBoxRect.Width);
        Assert.Equal("abcde", GetLines(document, "b")[0]);
        Assert.Equal(new PhysicalSize(65, 20), GetFragment(document, "c").Size);
        Assert.Equal(["abcde", "fg"], GetLines(document, "c"));
    }

    [Fact]
    public void TextField_LineIsCenteredAndScrollsInTheContentBox()
    {
        var document = Parse("<input id=i value='abc  defghijkl' style='width: 50px; height: 30px; padding: 0 7px'>");
        var input = document.GetElementById("i")!;
        Assert.Equal(["abc  defghijkl"], GetLines(document, "i"));

        var fragment = GetFragment(document, "i");
        var editor = TextControlLayout.GetInnerEditorFragment(fragment)!;
        Assert.Equal(new PhysicalRect(7, 10, 50, 10), new PhysicalRect(editor.Offset, editor.Size));
        Assert.Equal(18, fragment.FirstBaseline);

        // The scrolling box is the inner editor: the paddings of the inline axis are outside of it (csswg-drafts#13502)
        Assert.Equal((7, 50, 140, 30, 30), (input.ClientLeft, input.ClientWidth, input.ScrollWidth, input.ClientHeight, input.ScrollHeight));
        input.ScrollLeft = 1000;
        Assert.Equal(90, input.ScrollLeft);
        Assert.Contains(GetDrawings(document), line => line.StartsWith("draw-glyphs \"abc  defghijkl\" Ahem 10px at (-83, 18)", StringComparison.Ordinal));
    }

    [Fact]
    public void TextField_PasswordIsRenderedAsBullets()
    {
        var document = Parse("<input id=i type=password value=abc>");
        Assert.Equal(["•••"], GetLines(document, "i"));
    }

    [Fact]
    public void Placeholder_IsABoxOfThePseudoElement()
    {
        var document = Parse("<style>::placeholder { color: rgb(0, 0, 255); writing-mode: vertical-rl } input::placeholder { line-height: 0 }</style><input id=i placeholder='a b' style='height: 30px'><textarea id=t cols=3 placeholder='a b c d'></textarea>");
        var input = GetFragment(document, "i");
        var placeholder = TextControlLayout.GetPlaceholderFragment(input)!;
        Assert.Equal("placeholder", placeholder.Box.PseudoElement);

        // The placeholder of a text field is a line with the line height of the control, centered as the line of the inner
        // editor; the writing mode of the control applies to it
        Assert.Equal(new PhysicalRect(0, 10, input.Size.Width, 10), new PhysicalRect(placeholder.Offset, placeholder.Size));
        Assert.Contains("draw-glyphs \"a b\" Ahem 10px at (0, 18) glyphs=3 fill #0000ff  [input#i]", GetDrawings(document));

        // The placeholder of a textarea wraps as its value
        var lines = TextControlLayout.GetPlaceholderFragment(GetFragment(document, "t"))!.Children.OfType<LineBoxFragment>().Count();
        Assert.Equal(2, lines);

        // The placeholder is not rendered when the control has a value
        ((HtmlInputElement)document.GetElementById("i")!).Value = "x";
        Assert.Null(TextControlLayout.GetPlaceholderFragment(GetFragment(document, "i")));
        Assert.Equal(["x"], GetLines(document, "i"));
    }

    [Fact]
    public void Selection_IsHighlightedInTheLinesOfTheInnerEditor()
    {
        var document = Parse("<style>::selection { background: rgb(0, 0, 255); color: rgb(255, 255, 0) }</style><textarea id=t cols=4 rows=2 style='line-height: 20px'>abcdefg</textarea>");
        var textArea = (HtmlTextAreaElement)document.GetElementById("t")!;
        Assert.DoesNotContain(GetDrawings(document), line => line.Contains("#0000ff", StringComparison.Ordinal));

        // The highlight covers the line boxes; the selected glyphs have the color of ::selection
        textArea.SetSelectionRange(2, 6);
        var drawings = GetDrawings(document);
        Assert.Contains("draw-rect (20, 0, 20x20) fill #0000ff  [textarea#t]", drawings);
        Assert.Contains("draw-rect (0, 20, 20x20) fill #0000ff  [textarea#t]", drawings);
        Assert.Contains("draw-glyphs \"ab\" Ahem 10px at (0, 13) glyphs=2 fill #000000  [textarea#t]", drawings);
        Assert.Contains("draw-glyphs \"cd\" Ahem 10px at (20, 13) glyphs=2 fill #ffff00  [textarea#t]", drawings);
        Assert.Contains("draw-glyphs \"ef\" Ahem 10px at (0, 33) glyphs=2 fill #ffff00  [textarea#t]", drawings);
        Assert.Contains("draw-glyphs \"g\" Ahem 10px at (20, 33) glyphs=1 fill #000000  [textarea#t]", drawings);
    }

    [Fact]
    public void CaretPositionFromPoint_ReadsTheLineBoxes()
    {
        var document = Parse("<div style='height: 5px'></div><textarea id=t cols=4 rows=3 style='text-align: right'>ab cdefg\nh</textarea><br><input id=i value=abcdef style='width: 30px; height: 30px'><input id=e>");
        var textArea = document.GetElementById("t")!;

        // "ab " / "cdef" / "g" / "h", right aligned in 40px (the space at the end of the first line hangs)
        var position = document.CaretPositionFromPoint(29, 6)!;
        Assert.Same(textArea, position.OffsetNode);
        Assert.Equal(1u, position.Offset);
        Assert.Equal((30d, 5d, 0d, 10d), ToTuple(position.GetClientRect()!));
        Assert.Equal(0u, document.CaretPositionFromPoint(2, 6)!.Offset);
        Assert.Equal(5u, document.CaretPositionFromPoint(18, 19)!.Offset);
        Assert.Equal(8u, document.CaretPositionFromPoint(39, 28)!.Offset);

        // The line of a text field is centered; the positions follow its scroll position
        var input = document.GetElementById("i")!;
        position = document.CaretPositionFromPoint(11, 40)!;
        Assert.Same(input, position.OffsetNode);
        Assert.Equal(1u, position.Offset);
        Assert.Equal((10d, 45d, 0d, 10d), ToTuple(position.GetClientRect()!));
        input.ScrollLeft = 20;
        Assert.Equal(3u, document.CaretPositionFromPoint(11, 40)!.Offset);

        // An empty control: the start of the line of its inner editor
        position = document.CaretPositionFromPoint(40, 40)!;
        Assert.Same(document.GetElementById("e"), position.OffsetNode);
        Assert.Equal(0u, position.Offset);
    }

    [Fact]
    public void CaretKeys_ScrollTheValueToRevealTheCaret()
    {
        var document = Parse("<input id=i value=abcdefghij style='width: 30px'><textarea id=t cols=4 rows=2>a\nb\nc\nd</textarea>");
        var input = (HtmlInputElement)document.GetElementById("i")!;
        input.Focus();
        input.SetSelectionRange(2, 2);
        PressKey(input, "ArrowRight");
        Assert.Equal(0, input.ScrollLeft);
        PressKey(input, "ArrowRight");
        Assert.Equal((4u, 10d), (input.SelectionStart!.Value, input.ScrollLeft));
        PressKey(input, "End");
        Assert.Equal(70, input.ScrollLeft);
        PressKey(input, "Home");
        Assert.Equal(0, input.ScrollLeft);

        var textArea = (HtmlTextAreaElement)document.GetElementById("t")!;
        textArea.Focus();
        textArea.SetSelectionRange(0, 0);
        PressKey(textArea, "ArrowDown");
        Assert.Equal(0, textArea.ScrollTop);
        PressKey(textArea, "ArrowDown");
        Assert.Equal((4u, 10d), (textArea.SelectionStart, textArea.ScrollTop));

        static void PressKey(Node target, string key)
            => EventDispatcher.Dispatch(new KeyboardEvent("keydown", new KeyboardEventInit { Key = key, Bubbles = true, Cancelable = true }) { IsTrusted = true }, target);
    }

    [Fact]
    public void PointerSelection_ReadsTheLineBoxes_AndTheUserEditsAreLaidOut()
    {
        var document = Parse("<style>::selection { background: rgb(0, 0, 255); color: rgb(255, 255, 0) }</style><textarea id=t cols=4 rows=3 style='line-height: 20px'>abcdefgh</textarea>");
        var textArea = (HtmlTextAreaElement)document.GetElementById("t")!;
        var mouse = document.BrowsingContext!.Input.Mouse;

        // "abcd" / "efgh": a press in the second line puts the caret between its characters
        mouse.Move(11, 30);
        mouse.Down();
        Assert.Same(textArea, document.ActiveElement);
        Assert.Equal((5u, 5u), (textArea.SelectionStart, textArea.SelectionEnd));
        Assert.DoesNotContain(GetDrawings(document), line => line.Contains("#0000ff", StringComparison.Ordinal));

        // A drag to the first line extends the selection backward; the highlight is painted in the two lines
        mouse.Move(21, 10);
        Assert.Equal((2u, 5u, "backward"), (textArea.SelectionStart, textArea.SelectionEnd, textArea.SelectionDirection));
        var drawings = GetDrawings(document);
        Assert.Contains("draw-rect (20, 0, 20x20) fill #0000ff  [textarea#t]", drawings);
        Assert.Contains("draw-rect (0, 20, 10x20) fill #0000ff  [textarea#t]", drawings);
        mouse.Move(31, 10);
        Assert.Equal((3u, 5u), (textArea.SelectionStart, textArea.SelectionEnd));
        Assert.Contains("draw-rect (30, 0, 10x20) fill #0000ff  [textarea#t]", GetDrawings(document));
        mouse.Up();

        // The text typed by the user replaces the selection, and the lines are laid out again
        Assert.True(Meziantou.Framework.WebPlatform.Html.Editing.UserEditTextControl(textArea, "insertText", "X"));
        Assert.Equal("abcXfgh", textArea.Value);
        Assert.Equal((4u, 4u), (textArea.SelectionStart, textArea.SelectionEnd));
        Assert.Equal(["abcX", "fgh"], GetLines(document, "t"));
        Assert.DoesNotContain(GetDrawings(document), line => line.Contains("#0000ff", StringComparison.Ordinal));

        // The keys move the caret from the position of the press
        mouse.Move(31, 30);
        mouse.Down();
        mouse.Up();
        Assert.Equal((7u, 7u), (textArea.SelectionStart, textArea.SelectionEnd));
        EventDispatcher.Dispatch(new KeyboardEvent("keydown", new KeyboardEventInit { Key = "ArrowLeft", ShiftKey = true, Bubbles = true, Cancelable = true }) { IsTrusted = true }, textArea);
        Assert.Equal((6u, 7u), (textArea.SelectionStart, textArea.SelectionEnd));
        Assert.Contains("draw-rect (20, 20, 10x20) fill #0000ff  [textarea#t]", GetDrawings(document));
    }

    [Fact]
    public void ElementFromPoint_ReturnsTheControl()
    {
        var document = Parse("<textarea id=t cols=4 rows=3>abcd</textarea>");
        Assert.Same(document.GetElementById("t"), document.ElementFromPoint(5, 5));
        Assert.Same(document.GetElementById("t"), document.ElementFromPoint(5, 25));
    }

    [Fact]
    public void ValueChange_UpdatesTheLines()
    {
        var document = Parse("<textarea id=t cols=5>a</textarea>");
        var textArea = (HtmlTextAreaElement)document.GetElementById("t")!;
        textArea.Value = "abcdefgh";
        Assert.Equal(["abcde", "fgh"], GetLines(document, "t"));
        textArea.Value = "";
        Assert.Equal([""], GetLines(document, "t"));
    }

    [Fact]
    public void VerticalWritingMode_LaysOutTheLinesVertically()
    {
        var document = Parse("<textarea id=t cols=5 rows=2 style='writing-mode: vertical-rl'>abcdefg</textarea>");
        var fragment = GetFragment(document, "t");
        Assert.Equal(new PhysicalSize(20, 50), fragment.Size);
        var editor = TextControlLayout.GetInnerEditorFragment(fragment)!;
        Assert.Equal(new PhysicalRect(0, 0, 20, 50), new PhysicalRect(editor.Offset, editor.Size));
        Assert.Equal(["abcde", "fg"], GetLines(document, "t"));
    }
}
