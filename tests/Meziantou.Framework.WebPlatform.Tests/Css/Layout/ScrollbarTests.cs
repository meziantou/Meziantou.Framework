using Meziantou.Framework.WebPlatform.Dom;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Layout;

/// <summary>
/// The classic scrollbars (https://drafts.csswg.org/css-overflow-3/#classic-scrollbars, https://drafts.csswg.org/css-scrollbars-1/):
/// their painting in the scrollbar gutters (checked with the dumps of the display lists) and the presses of the pointer on
/// their track and their thumb.
/// </summary>
public sealed class ScrollbarTests : IDisposable
{
    private const string Track = "#f1f1f1";
    private const string Thumb = "#c1c1c1";

    private readonly BrowsingContextScope _contexts = new();
    private readonly FakeTimeProvider _clock = new();

    public void Dispose() => _contexts.Dispose();

    /// <summary>Parses a no-quirks document whose body has no margin, in a 800x600 viewport with classic scrollbars of 15px.</summary>
    private Document Parse(string html, ViewportOptions? viewport = null)
    {
        var context = _contexts.Create(new BrowsingContextOptions { TimeProvider = _clock, Viewport = viewport ?? new ViewportOptions { Width = 800, Height = 600 } });
        var document = context.Navigate("https://example.com/", _ => "<!DOCTYPE html><style>body { margin: 0 } :focus { outline: none }</style>" + html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    /// <summary>A scroll container of 100x100px with both scrollbars forced, whose content is 300px tall: it scrolls by 215px in the y axis.</summary>
    private const string Scroller = "<div id=s style='width: 100px; height: 100px; overflow: scroll'><div id=c style='height: 300px'></div></div>";

    private static HtmlElement Get(Document document, string id) => (HtmlElement)document.GetElementById(id)!;

    /// <summary>Returns the rectangles painted by the display list of the document.</summary>
    private static List<string> GetRects(Document document)
        => [.. document.GetDisplayList()!.Dump().Split('\n').Select(line => line.Trim()).Where(line => line.StartsWith("draw-rect", StringComparison.Ordinal))];

    private void RunFrames(int count = 2)
    {
        for (var i = 0; i < count; i++)
        {
            _clock.Advance(TimeSpan.FromMilliseconds(17));
            _contexts.EventLoop.RunUntilIdle();
        }
    }

    [Fact]
    public void Scrollbars_ArePaintedInTheGutters()
    {
        var document = Parse(Scroller);

        // The track of each scrollbar, the thumb of the axis that scrolls (inset by 2px; 85px * 85 / 300 long) and the corner
        Assert.Equal(
            [
                $"draw-rect (85, 0, 15x85) fill {Track}  [div#s]",
                $"draw-rect (87, 0, 11x24) fill {Thumb}  [div#s]",
                $"draw-rect (0, 85, 85x15) fill {Track}  [div#s]",
                $"draw-rect (85, 85, 15x15) fill {Track}  [div#s]",
            ],
            GetRects(document));

        // The thumb follows the scroll position
        Get(document, "s").ScrollTop = 215;
        Assert.Contains($"draw-rect (87, 61, 11x24) fill {Thumb}  [div#s]", GetRects(document));
    }

    [Fact]
    public void Scrollbars_FollowTheDirectionAndTheAutomaticScrollbars()
    {
        // The vertical scrollbar of a right-to-left box is on the left; an automatic scrollbar is only displayed when the box overflows
        var document = Parse("""
            <div id=r style='width: 100px; height: 100px; overflow: auto; direction: rtl'><div style='height: 300px'></div></div>
            <div id=n style='width: 100px; height: 100px; overflow: auto; scrollbar-gutter: stable'></div>
            """);
        Assert.Equal(
            [
                $"draw-rect (0, 0, 15x100) fill {Track}  [div#r]",
                $"draw-rect (2, 0, 11x33) fill {Thumb}  [div#r]",
            ],
            GetRects(document));
    }

    [Fact]
    public void ScrollbarColorAndWidth_StyleTheScrollbars()
    {
        var document = Parse($"""
            <style>div {"{"} width: 100px; height: 100px; overflow-y: scroll {"}"} p {"{"} height: 300px; margin: 0 {"}"}</style>
            <div id=a style='scrollbar-color: rgb(255, 0, 0) rgb(0, 0, 255)'><p></p></div>
            <div id=b style='scrollbar-width: thin; color: rgb(0, 128, 0); scrollbar-color: currentcolor transparent'><p></p></div>
            <div id=c style='scrollbar-width: none'><p></p></div>
            <div id=d style='color-scheme: dark'><p></p></div>
            <div id=e style='scrollbar-color: transparent transparent'><p></p></div>
            """);
        Assert.Equal(
            [
                "draw-rect (85, 0, 15x100) fill #0000ff  [div#a]",
                "draw-rect (87, 0, 11x33) fill #ff0000  [div#a]",

                // A thin scrollbar is 11px thick; its track is transparent
                "draw-rect (91, 100, 7x33) fill #008000  [div#b]",

                // (no scrollbar for scrollbar-width: none) The scrollbars of a dark color scheme
                "draw-rect (85, 300, 15x100) fill #2c2c2c  [div#d]",
                "draw-rect (87, 300, 11x33) fill #686868  [div#d]",
            ],
            GetRects(document));
    }

    [Fact]
    public void Viewport_PaintsItsScrollbarsOverTheDocument()
    {
        var document = Parse("<style>html { scrollbar-color: rgb(255, 0, 0) rgb(0, 0, 255); background: rgb(0, 255, 0) }</style><div style='height: 1200px'></div>");
        var displayList = document.GetDisplayList()!;

        // The display list is the viewport, with the gutter of its scrollbar: the document is clipped to the scrollport
        Assert.Equal((800d, 600d), (displayList.Size.Width, displayList.Size.Height));
        Assert.Equal(785, document.DocumentElement!.ClientWidth);
        var dump = displayList.Dump();
        Assert.Contains("clip-rect (0, 0, 785x600)", dump, StringComparison.Ordinal);
        Assert.Equal(
            [
                "draw-rect (785, 0, 15x600) fill #0000ff  [html]",
                "draw-rect (787, 0, 11x300) fill #ff0000  [html]",
            ],
            GetRects(document));

        // The background of the canvas is painted under the gutter, before the clip of the scrollport
        Assert.True(dump.IndexOf("draw-paint", StringComparison.Ordinal) < dump.IndexOf("clip-rect (0, 0, 785x600)", StringComparison.Ordinal));
    }

    [Fact]
    public void ScrollbarsVisible_False_LeavesTheGuttersEmpty()
    {
        var document = Parse(Scroller + "<div style='height: 1200px'></div>", new ViewportOptions { ScrollbarsVisible = false });
        Assert.Empty(GetRects(document));

        // The scrollbars still take their space, and the display list is still the viewport
        Assert.Equal(85, Get(document, "s").ClientWidth);
        Assert.Equal(800, document.GetDisplayList()!.Size.Width);

        // They do not react to the pointer
        var mouse = document.BrowsingContext!.Input.Mouse;
        mouse.Move(92, 80);
        mouse.Down();
        mouse.Up();
        Assert.Equal(0, Get(document, "s").ScrollTop);
    }

    [Fact]
    public void PressOnTheTrack_ScrollsByAPage()
    {
        var document = Parse(Scroller);
        var scroller = Get(document, "s");
        var events = new List<string>();
        foreach (var type in new[] { "pointerdown", "mousedown", "pointermove", "mousemove", "pointerup", "mouseup", "click", "scroll", "scrollend" })
        {
            scroller.AddEventListener(type, e => events.Add(e.Type));
        }

        // A press below the thumb scrolls down by a page (87.5% of the scrollport)
        var mouse = document.BrowsingContext!.Input.Mouse;
        mouse.Move(92, 80);
        events.Clear();
        mouse.Down();
        mouse.Up();
        RunFrames();
        Assert.Equal(74.375, scroller.ScrollTop);

        // The events of the press and of the release are fired at the scroll container, without click
        Assert.Equal(["pointerdown", "mousedown", "pointerup", "mouseup", "scroll", "scrollend"], events);

        // A press above the thumb scrolls up
        mouse.Move(92, 2);
        mouse.Down();
        mouse.Up();
        Assert.Equal(0, scroller.ScrollTop);

        // The scrollbar of an axis that cannot be scrolled does nothing
        mouse.Move(40, 92);
        mouse.Down();
        mouse.Up();
        Assert.Equal(0, scroller.ScrollLeft);
    }

    [Fact]
    public void DragOfTheThumb_ScrollsWithoutPointerEvents()
    {
        var document = Parse(Scroller + "<p id=t>text</p>");
        var scroller = Get(document, "s");
        var events = new List<string>();
        foreach (var type in new[] { "pointerdown", "pointermove", "mousemove", "pointerup", "click" })
        {
            document.AddEventListener(type, e => events.Add(e.Type));
        }

        // The thumb (24.08px long on a track of 85px) moves by 30px: the box scrolls by 30 * 215 / (85 - 24.08)
        var mouse = document.BrowsingContext!.Input.Mouse;
        mouse.Move(92, 10);
        events.Clear();
        mouse.Down();
        mouse.Move(92, 40);
        Assert.Equal(105.88, scroller.ScrollTop, 2);

        // The pointer may leave the scrollbar; the scroll position is clamped
        mouse.Move(300, 400);
        Assert.Equal(215, scroller.ScrollTop);
        mouse.Move(300, 10);
        Assert.Equal(0, scroller.ScrollTop);
        mouse.Up();

        // The scrollbar captured the pointer: no move event during the drag, no click, and no selection
        Assert.Equal(["pointerdown", "pointerup"], events);
        Assert.True(document.GetSelection()!.IsCollapsed);

        // After the release the pointer moves over the content again
        mouse.Move(10, 110);
        Assert.Contains("pointermove", events);
    }

    [Fact]
    public void PressOnAScrollbar_FocusesAsThePressOnItsBox()
    {
        var document = Parse("<input id=i>" + Scroller.Replace("id=s", "id=s tabindex=0", StringComparison.Ordinal) + "<div style='height: 1200px'></div>");
        var input = Get(document, "i");
        var scroller = Get(document, "s");
        var top = scroller.GetBoundingClientRect().Top;
        var mouse = document.BrowsingContext!.Input.Mouse;

        // A press on a scrollbar of the viewport keeps the focus where it is, and scrolls the viewport
        input.Focus();
        mouse.Move(792, 500);
        mouse.Down();
        mouse.Up();
        Assert.Same(input, document.ActiveElement);
        Assert.Equal(525, document.DefaultView!.ScrollY);
        _ = document.DefaultView.ScrollTo(0, 0);

        // A press on the scrollbar of a scroll container focuses it as a press on its box does
        mouse.Move(92, top + 80);
        mouse.Down();
        mouse.Up();
        Assert.Same(scroller, document.ActiveElement);
        Assert.Equal(74.375, scroller.ScrollTop);
    }

    [Fact]
    public void ScrollbarOfAnInertElement_IsNotInteractive()
    {
        var document = Parse(Scroller);
        var scroller = Get(document, "s");
        scroller.SetAttribute("inert", "");
        var mouse = document.BrowsingContext!.Input.Mouse;
        mouse.Move(92, 10);
        mouse.Down();
        mouse.Move(92, 60);
        mouse.Up();
        Assert.Equal(0, scroller.ScrollTop);

        scroller.RemoveAttribute("inert");
        mouse.Move(92, 10);
        mouse.Down();
        mouse.Move(92, 60);
        mouse.Up();
        Assert.True(scroller.ScrollTop > 0);
    }

    [Fact]
    public void AutomaticScrollbar_IsNotDisplayedByAScrollportWithoutSize()
    {
        // The box is 0px tall: its vertical scrollbar narrows it, but the horizontal scrollbar that the narrower scrollport
        // would need is not displayed (it would give its thickness to a box that shows nothing)
        var document = Parse("<div id=z style='width: 300px; overflow: auto'><div style='height: 0; position: relative'><div style='position: absolute; width: 300px; height: 300px'></div></div></div>");
        var box = Get(document, "z");
        Assert.Equal(0, box.OffsetHeight);
        Assert.Equal(285, box.ClientWidth);
        Assert.Empty(GetRects(document));
    }

    [Fact]
    public void BodyWithDisplayContents_DoesNotPropagateItsOverflowToTheViewport()
    {
        var context = _contexts.Create(new BrowsingContextOptions { TimeProvider = _clock, Viewport = new ViewportOptions { Width = 800, Height = 600 } });
        var document = context.Navigate("https://example.com/", _ => "<!DOCTYPE html><body style='display: contents; overflow: scroll'>");
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal(800, document.DocumentElement!.ClientWidth);
        Assert.Empty(GetRects(document));
    }

    [Fact]
    public void ScrollingAttribute_HidesTheScrollbarsOfTheViewportOfAFrame()
    {
        var document = Parse("""
            <iframe id=a srcdoc="<body style='margin: 0'><div style='height: 1000px'></div>" style='border: 0; width: 200px; height: 100px'></iframe>
            <iframe id=b scrolling=no srcdoc="<body style='margin: 0'><div style='height: 1000px'></div>" style='border: 0; width: 200px; height: 100px'></iframe>
            """);
        RunFrames();
        var a = ((HtmlIFrameElement)Get(document, "a")).ContentDocument!;
        var b = ((HtmlIFrameElement)Get(document, "b")).ContentDocument!;
        Assert.Equal(185, a.DocumentElement!.ClientWidth);
        Assert.Equal(200, b.DocumentElement!.ClientWidth);
    }
}
