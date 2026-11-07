using Meziantou.Framework.WebPlatform.Dom;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Layout;

/// <summary>
/// The painting of the text insertion caret (https://drafts.csswg.org/css-ui-4/#insertion-caret): the caret of the collapsed
/// selection in the focused editable content and of the focused text controls, <c>caret-color</c>, <c>caret-shape</c>,
/// <c>caret-animation</c> and the options of the host, checked with the dumps of the display lists.
/// </summary>
public sealed class CaretPaintingTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly FakeTimeProvider _clock = new();

    public void Dispose() => _contexts.Dispose();

    /// <summary>
    /// Parses a no-quirks document whose body has no margin and the 10px/1 Ahem font, in a 800x600 viewport; the text controls
    /// have the same font, no border, no padding, no background and no outline.
    /// </summary>
    private Document Parse(string html, ViewportOptions? viewport = null)
    {
        var context = _contexts.Create(new BrowsingContextOptions { TimeProvider = _clock, Viewport = viewport ?? new ViewportOptions { Width = 800, Height = 600 } });
        var document = context.Navigate("https://example.com/", _ => "<!DOCTYPE html><style>body { margin: 0; font: 10px/1 Ahem } :focus { outline: none } textarea, input { font: 10px/1 Ahem; border: 0; padding: 0; margin: 0; vertical-align: top; background: none }</style>" + html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    private static HtmlElement Get(Document document, string id) => (HtmlElement)document.GetElementById(id)!;

    /// <summary>Focuses an editing host and collapses the selection in its first child.</summary>
    private static void SetCaret(Document document, string id, uint offset)
    {
        var host = Get(document, id);
        host.Focus();
        document.GetSelection()!.Collapse(host.FirstChild ?? host, offset);
    }

    /// <summary>Returns the rectangles painted by the display list of the document.</summary>
    private static List<string> GetRects(Document document)
        => [.. document.GetDisplayList()!.Dump().Split('\n').Select(line => line.Trim()).Where(line => line.StartsWith("draw-rect", StringComparison.Ordinal))];

    private static List<string> GetGlyphs(Document document)
        => [.. document.GetDisplayList()!.Dump().Split('\n').Select(line => line.Trim()).Where(line => line.StartsWith("draw-glyphs", StringComparison.Ordinal))];

    [Fact]
    public void Bar_IsPaintedAtTheFocusOfTheCollapsedSelection()
    {
        var document = Parse("<div id=a contenteditable>abcd</div><div id=b contenteditable style='color: rgb(0, 0, 255)'>efgh</div>");
        Assert.Empty(GetRects(document));

        // The caret is a bar of 1px as tall as the text, in the color of the text, painted after the glyphs
        SetCaret(document, "a", 2);
        Assert.Equal(["draw-rect (20, 0, 1x10) fill #000000  [#text]"], GetRects(document));
        var dump = document.GetDisplayList()!.Dump();
        Assert.True(dump.IndexOf("draw-glyphs \"abcd\"", StringComparison.Ordinal) < dump.IndexOf("draw-rect (20, 0, 1x10)", StringComparison.Ordinal));

        // The caret follows the selection and the focus
        SetCaret(document, "b", 4);
        Assert.Equal(["draw-rect (40, 10, 1x10) fill #0000ff  [#text]"], GetRects(document));
    }

    [Fact]
    public void Caret_IsNotPaintedForARangeNorWithoutTheFocus()
    {
        var document = Parse("<style>::selection { background: rgb(0, 255, 0) }</style><div id=a contenteditable>abcd</div><input id=i readonly value=x><span id=o tabindex=0>x</span><div id=n>efgh</div>");
        SetCaret(document, "a", 1);
        Assert.Contains("draw-rect (10, 0, 1x10) fill #000000  [#text]", GetRects(document));

        // The window lost the system focus
        document.BrowsingContext!.HasSystemFocus = false;
        Assert.DoesNotContain(GetRects(document), rect => rect.Contains("1x10", StringComparison.Ordinal));
        document.BrowsingContext.HasSystemFocus = true;
        Assert.Contains("draw-rect (10, 0, 1x10) fill #000000  [#text]", GetRects(document));

        // An element outside of the editing host is focused
        Get(document, "o").Focus();
        Assert.DoesNotContain(GetRects(document), rect => rect.Contains("1x10", StringComparison.Ordinal));

        // A selection that is not collapsed is highlighted: it has no caret
        Get(document, "a").Focus();
        var text = Get(document, "a").FirstChild!;
        document.GetSelection()!.SetBaseAndExtent(text, 1, text, 3);
        Assert.Equal(["draw-rect (10, 0, 20x10) fill #00ff00  [#text]"], GetRects(document));

        // A collapsed selection in content that is not editable has no caret, nor a text control that cannot be edited
        Get(document, "a").Blur();
        document.GetSelection()!.Collapse(Get(document, "n").FirstChild, 1);
        Assert.Empty(GetRects(document));
        Get(document, "i").Focus();
        Assert.Empty(GetRects(document));
    }

    [Fact]
    public void Caret_IsNotPaintedWhenTheHostHidesIt()
    {
        var document = Parse("<div id=a contenteditable>abcd</div>", new ViewportOptions { CaretVisible = false });
        SetCaret(document, "a", 2);
        Assert.Empty(GetRects(document));
    }

    [Fact]
    public void CaretColor_IsTheColorOfTheCaret()
    {
        var document = Parse("<div id=a contenteditable style='caret-color: rgb(255, 0, 0)'>abcd</div><div id=b contenteditable style='caret-color: transparent'>abcd</div>");
        SetCaret(document, "a", 0);
        Assert.Equal(["draw-rect (0, 0, 1x10) fill #ff0000  [#text]"], GetRects(document));
        SetCaret(document, "b", 0);
        Assert.Empty(GetRects(document));
    }

    [Fact]
    public void Block_CoversTheNextCharacterUnderItsGlyph()
    {
        var document = Parse("""
            <div id=a contenteditable style='caret-shape: block; caret-color: rgb(255, 0, 0) rgb(0, 255, 0)'>abcd</div>
            <div id=b contenteditable style='caret-shape: block; caret-color: rgb(255, 0, 0)'>abcd</div>
            <div id=c contenteditable style='caret-shape: block; color: rgb(0, 0, 255)'>abcd</div>
            """);

        // The second color of caret-color is the color of the character under the caret, whose glyph is painted once
        SetCaret(document, "a", 1);
        var dump = document.GetDisplayList()!.Dump();
        Assert.Contains("draw-rect (10, 0, 10x10) fill #ff0000  [#text]", dump, StringComparison.Ordinal);
        Assert.Contains("draw-glyphs \"a\" Ahem 10px at (0, 8) glyphs=1 fill #000000  [#text]", dump, StringComparison.Ordinal);
        Assert.Contains("draw-glyphs \"b\" Ahem 10px at (10, 8) glyphs=1 fill #00ff00  [#text]", dump, StringComparison.Ordinal);
        Assert.Contains("draw-glyphs \"cd\" Ahem 10px at (20, 8) glyphs=2 fill #000000  [#text]", dump, StringComparison.Ordinal);
        Assert.True(dump.IndexOf("draw-rect (10, 0, 10x10)", StringComparison.Ordinal) < dump.IndexOf("draw-glyphs \"b\"", StringComparison.Ordinal));

        // Without second color, the character keeps its color
        SetCaret(document, "b", 1);
        Assert.Equal(["draw-rect (10, 10, 10x10) fill #ff0000  [#text]"], GetRects(document));
        Assert.Contains("draw-glyphs \"abcd\" Ahem 10px at (0, 18) glyphs=4 fill #000000  [#text]", GetGlyphs(document));

        // An auto block caret has the color of the text, half transparent
        SetCaret(document, "c", 1);
        Assert.Equal(["draw-rect (10, 20, 10x10) fill #0000ff80  [#text]"], GetRects(document));
    }

    [Fact]
    public void BlockAndUnderscore_AreOneChLongWithoutNextCharacter()
    {
        var document = Parse("""
            <div id=a contenteditable style='caret-shape: block; caret-color: rgb(255, 0, 0); width: 100px'>ab</div>
            <div id=b contenteditable style='caret-shape: underscore; caret-color: rgb(255, 0, 0); width: 100px'>ab</div>
            """);
        SetCaret(document, "a", 2);
        Assert.Equal(["draw-rect (20, 0, 10x10) fill #ff0000  [#text]"], GetRects(document));

        // The underscore is a line of 1px under the character
        SetCaret(document, "b", 1);
        Assert.Equal(["draw-rect (10, 19, 10x1) fill #ff0000  [#text]"], GetRects(document));
        SetCaret(document, "b", 2);
        Assert.Equal(["draw-rect (20, 19, 10x1) fill #ff0000  [#text]"], GetRects(document));
    }

    [Fact]
    public void Caret_AtTheEndOfAFullLineStaysInItsContainer()
    {
        var document = Parse("""
            <div id=a contenteditable style='width: 40px; overflow: hidden'>abcd</div>
            <div id=b contenteditable style='width: 40px; overflow: hidden; caret-shape: block'>abcd</div>
            <div id=c contenteditable style='width: 40px; white-space: pre'>abcdef</div>
            """);
        SetCaret(document, "a", 4);
        Assert.Equal(["draw-rect (39, 0, 1x10) fill #000000  [#text]"], GetRects(document));
        SetCaret(document, "b", 4);
        Assert.Equal(["draw-rect (30, 10, 10x10) fill #00000080  [#text]"], GetRects(document));

        // The caret follows a line that overflows its container
        SetCaret(document, "c", 6);
        Assert.Equal(["draw-rect (59, 20, 1x10) fill #000000  [#text]"], GetRects(document));
    }

    [Fact]
    public void EmptyEditingHost_HasTheLineOfItsCaret()
    {
        var document = Parse("""
            <div id=a contenteditable style='width: 100px; line-height: 20px'></div>
            <div id=b contenteditable style='width: 100px; direction: rtl'></div>
            <div id=c contenteditable style='width: 100px; text-align: center; caret-shape: block; caret-color: rgb(255, 0, 0)'> </div>
            <div id=d contenteditable style='width: 100px; text-indent: 30px'></div>
            <div id=n style='width: 100px'></div>
            """);

        // An empty editing host is as tall as a line (an empty block that is not editable has no height)
        Assert.Equal(20, Get(document, "a").OffsetHeight);
        Assert.Equal(10, Get(document, "b").OffsetHeight);
        Assert.Equal(10, Get(document, "c").OffsetHeight);
        Assert.Equal(0, Get(document, "n").OffsetHeight);

        // The caret is as tall as the font, centered in the line, at the position given by the direction, text-align and text-indent
        SetCaret(document, "a", 0);
        Assert.Equal(["draw-rect (0, 5, 1x10) fill #000000  [div#a]"], GetRects(document));
        SetCaret(document, "b", 0);
        Assert.Equal(["draw-rect (99, 20, 1x10) fill #000000  [div#b]"], GetRects(document));
        SetCaret(document, "c", 0);
        Assert.Single(GetRects(document), rect => rect.StartsWith("draw-rect (50, 30, 10x10) fill #ff0000", StringComparison.Ordinal));
        SetCaret(document, "d", 0);
        Assert.Equal(["draw-rect (30, 40, 1x10) fill #000000  [div#d]"], GetRects(document));
    }

    [Fact]
    public void EmptyEditingHost_FlexAndGridContainersHaveTheLineOfTheirCaret()
    {
        var document = Parse("""
            <div id=f contenteditable style='display: flex; line-height: 20px'></div>
            <div id=g contenteditable style='display: grid; line-height: 20px'></div>
            <div id=r contenteditable style='display: grid; grid-template-rows: 30px; line-height: 20px'></div>
            <div id=l contenteditable style='display: grid-lanes; line-height: 20px'></div>
            <div id=i contenteditable style='display: flex; line-height: 20px'><span style='height: 5px'></span></div>
            <div id=n style='display: flex; line-height: 20px'></div>
            """);

        // As in Blink, an editing host without items is at least as tall as a line (the rows of a grid may be taller); not with an item
        Assert.Equal(20, Get(document, "f").OffsetHeight);
        Assert.Equal(20, Get(document, "g").OffsetHeight);
        Assert.Equal(30, Get(document, "r").OffsetHeight);
        Assert.Equal(20, Get(document, "l").OffsetHeight);
        Assert.Equal(5, Get(document, "i").OffsetHeight);
        Assert.Equal(0, Get(document, "n").OffsetHeight);
    }

    [Fact]
    public void Caret_FollowsTheWritingModeAndTheDirection()
    {
        var document = Parse("""
            <div id=a contenteditable style='writing-mode: vertical-rl; height: 100px; width: 30px'>abcd</div>
            <div id=b contenteditable style='direction: rtl; width: 100px'>abcd</div>
            <div id=c contenteditable style='writing-mode: vertical-lr; height: 100px; width: 30px; caret-shape: underscore'>abcd</div>
            """);

        // In a vertical line the bar is horizontal
        SetCaret(document, "a", 2);
        Assert.Equal(["draw-rect (20, 20, 10x1) fill #000000  [#text]"], GetRects(document));

        // Left-to-right text in a right-to-left block: the text is at the line-right edge, the caret at the start of the text
        SetCaret(document, "b", 0);
        Assert.Equal(["draw-rect (60, 100, 1x10) fill #000000  [#text]"], GetRects(document));

        // The under side of a vertical line is its left side
        SetCaret(document, "c", 1);
        Assert.Equal(["draw-rect (0, 120, 1x10) fill #000000  [#text]"], GetRects(document));
    }

    [Fact]
    public void TextControl_PaintsTheCaretOfItsValue()
    {
        var document = Parse("""
            <input id=i value=abcd style='caret-color: rgb(255, 0, 0); height: 10px'><br>
            <textarea id=t cols=10 rows=3>ab&#10;</textarea><br>
            <textarea id=e cols=10 rows=2 style='caret-shape: block; caret-color: rgb(0, 0, 255)'></textarea>
            """);
        var input = (HtmlInputElement)Get(document, "i");
        input.Focus();
        input.SetSelectionRange(2, 2);
        Assert.Equal(["draw-rect (20, 0, 1x10) fill #ff0000  [input#i]"], GetRects(document));

        // A selection of the value has no caret
        input.SetSelectionRange(1, 3);
        Assert.DoesNotContain(GetRects(document), rect => rect.Contains("1x10", StringComparison.Ordinal));
        input.SetSelectionRange(0, 0);

        // The caret at the end of a value that ends with a line feed is on the empty last line
        var textArea = (HtmlTextAreaElement)Get(document, "t");
        textArea.Focus();
        textArea.SetSelectionRange(3, 3);
        Assert.Equal(["draw-rect (0, 20, 1x10) fill #000000  [textarea#t]"], GetRects(document));
        textArea.SetSelectionRange(2, 2);
        Assert.Equal(["draw-rect (20, 10, 1x10) fill #000000  [textarea#t]"], GetRects(document));

        // An empty control has the caret of its line
        Get(document, "e").Focus();
        Assert.Equal(["draw-rect (0, 40, 10x10) fill #0000ff  [textarea#e]"], GetRects(document));
    }

    [Fact]
    public void CaretAnimation_BlinksWithTheTimersOfTheEventLoop()
    {
        var document = Parse(
            "<div id=a contenteditable>abcd</div><div id=m contenteditable style='caret-animation: manual'>abcd</div>",
            new ViewportOptions { CaretBlinkInterval = TimeSpan.FromMilliseconds(500) });
        var eventLoop = _contexts.EventLoop;
        SetCaret(document, "a", 2);
        Assert.Single(GetRects(document));

        // The caret is hidden after the interval, then displayed again: the state only changes with the timers of the event loop
        _clock.Advance(TimeSpan.FromMilliseconds(499));
        eventLoop.RunUntilIdle();
        Assert.Single(GetRects(document));
        _clock.Advance(TimeSpan.FromMilliseconds(1));
        eventLoop.RunUntilIdle();
        Assert.Empty(GetRects(document));
        _clock.Advance(TimeSpan.FromMilliseconds(500));
        eventLoop.RunUntilIdle();
        Assert.Single(GetRects(document));
        _clock.Advance(TimeSpan.FromMilliseconds(500));
        eventLoop.RunUntilIdle();
        Assert.Empty(GetRects(document));

        // A caret that moves is displayed, and the blinking starts again
        document.GetSelection()!.Collapse(Get(document, "a").FirstChild, 3);
        Assert.Equal(["draw-rect (30, 0, 1x10) fill #000000  [#text]"], GetRects(document));
        _clock.Advance(TimeSpan.FromMilliseconds(499));
        eventLoop.RunUntilIdle();
        Assert.Single(GetRects(document));
        _clock.Advance(TimeSpan.FromMilliseconds(1));
        eventLoop.RunUntilIdle();
        Assert.Empty(GetRects(document));

        // The blinking stops when nothing is painted: no timer is left, and the caret is displayed by the next painting
        _clock.Advance(TimeSpan.FromMilliseconds(500));
        eventLoop.RunUntilIdle();
        _clock.Advance(TimeSpan.FromMilliseconds(500));
        eventLoop.RunUntilIdle();
        Assert.Null(eventLoop.PromoteDueTimers());
        Assert.Single(GetRects(document));

        // caret-animation: manual: the caret does not blink
        SetCaret(document, "m", 1);
        Assert.Single(GetRects(document));
        _clock.Advance(TimeSpan.FromMilliseconds(500));
        eventLoop.RunUntilIdle();
        Assert.Single(GetRects(document));
        _clock.Advance(TimeSpan.FromMilliseconds(500));
        eventLoop.RunUntilIdle();
        Assert.Null(eventLoop.PromoteDueTimers());
    }
}
