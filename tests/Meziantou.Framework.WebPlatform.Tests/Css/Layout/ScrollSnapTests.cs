using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Dom;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Layout;

/// <summary>
/// Scroll snapping (https://drafts.csswg.org/css-scroll-snap-1/): the snap positions of the programmatic scrolls, the re-snap
/// after the layout changes, and the snap events (https://drafts.csswg.org/css-scroll-snap-2/#snap-events).
/// </summary>
public sealed class ScrollSnapTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly FakeTimeProvider _clock = new();

    public void Dispose() => _contexts.Dispose();

    /// <summary>Parses a no-quirks document whose body has no margin, in a 800x600 viewport.</summary>
    private Document Parse(string html)
    {
        var context = _contexts.Create(new BrowsingContextOptions
        {
            TimeProvider = _clock,
            Viewport = new ViewportOptions { Width = 800, Height = 600 },
        });
        var document = context.Navigate("https://example.com/", _ => "<!DOCTYPE html><style>body { margin: 0 } div { box-sizing: border-box }</style>" + html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    /// <summary>Runs the rendering steps of the next frames.</summary>
    private void RunFrames(int count = 1)
    {
        for (var i = 0; i < count; i++)
        {
            _clock.Advance(TimeSpan.FromMilliseconds(17));
            _contexts.EventLoop.RunUntilIdle();
        }
    }

    // A 100x100 scroller without scrollbars, whose snap areas are 100px high, at 0, 100, 200... (the scroll range is 900px)
    private static string VerticalScroller(string type = "y mandatory", int count = 10, string itemStyle = "")
    {
        var items = string.Concat(Enumerable.Range(0, count).Select(i => $"<div id=item{i} style='height: 100px; scroll-snap-align: start; {itemStyle}'></div>"));
        return $"<div id=scroller style='width: 100px; height: 100px; overflow: hidden; scroll-snap-type: {type}'>{items}</div>";
    }

    [Fact]
    public void ScrollTo_SnapsToTheClosestSnapPosition()
    {
        var document = Parse(VerticalScroller());
        var scroller = document.GetElementById("scroller")!;

        scroller.ScrollTop = 140;
        Assert.Equal(100, scroller.ScrollTop);

        _ = scroller.ScrollTo(new ScrollToOptions { Top = 260 });
        Assert.Equal(300, scroller.ScrollTop);
    }

    [Fact]
    public void ScrollBy_SnapsAheadInTheDirectionOfTheScroll()
    {
        var document = Parse(VerticalScroller());
        var scroller = document.GetElementById("scroller")!;

        // The closest snap position is the current one (0), but a relative scroll snaps ahead
        _ = scroller.ScrollBy(new ScrollToOptions { Top = 10 });
        Assert.Equal(100, scroller.ScrollTop);

        _ = scroller.ScrollBy(new ScrollToOptions { Top = -10 });
        Assert.Equal(0, scroller.ScrollTop);
    }

    [Fact]
    public void ScrollBy_DoesNotPassOverASnapStop()
    {
        var document = Parse(VerticalScroller());
        var scroller = document.GetElementById("scroller")!;
        document.GetElementById("item2")!.SetAttribute("style", "height: 100px; scroll-snap-align: start; scroll-snap-stop: always");

        _ = scroller.ScrollBy(new ScrollToOptions { Top = 550 });
        Assert.Equal(200, scroller.ScrollTop);

        // An absolute scroll does not stop
        _ = scroller.ScrollTo(new ScrollToOptions { Top = 0 });
        _ = scroller.ScrollTo(new ScrollToOptions { Top = 550 });
        Assert.Equal(500, scroller.ScrollTop);
    }

    [Fact]
    public void Proximity_SnapsOnlyWithinAThirdOfTheSnapport()
    {
        var document = Parse("<div id=scroller style='width: 100px; height: 300px; overflow: hidden; scroll-snap-type: y'><div style='height: 300px; scroll-snap-align: start'></div><div style='height: 2000px'></div></div>");
        var scroller = document.GetElementById("scroller")!;

        scroller.ScrollTop = 90;
        Assert.Equal(0, scroller.ScrollTop);

        scroller.ScrollTop = 110;
        Assert.Equal(110, scroller.ScrollTop);
    }

    [Fact]
    public void FirstLayout_SnapsAMandatorySnapContainer()
    {
        var document = Parse("<div id=scroller style='width: 100px; height: 100px; overflow: hidden; scroll-snap-type: y mandatory'><div style='height: 150px'></div><div style='height: 100px; scroll-snap-align: start'></div><div style='height: 500px'></div></div>");

        Assert.Equal(150, document.GetElementById("scroller")!.ScrollTop);
    }

    [Fact]
    public void LayoutChange_ResnapsToTheSameSnapTarget()
    {
        var document = Parse("<div id=scroller style='width: 100px; height: 100px; overflow: hidden; scroll-snap-type: y mandatory'><div id=changer style='height: 100px'></div>" + string.Concat(Enumerable.Repeat("<div style='height: 100px; scroll-snap-align: start'></div>", 3)) + "</div>");
        var scroller = document.GetElementById("scroller")!;
        scroller.ScrollTop = 200;
        Assert.Equal(200, scroller.ScrollTop);

        // The snap target moves up: the scroll position follows it at once
        document.GetElementById("changer")!.SetAttribute("style", "height: 0");
        Assert.Equal(100, scroller.ScrollTop);
    }

    [Fact]
    public void ScrollPadding_ReducesTheSnapport()
    {
        var document = Parse(VerticalScroller(type: "y mandatory; scroll-padding-top: 20px"));
        var scroller = document.GetElementById("scroller")!;

        scroller.ScrollTop = 280;
        Assert.Equal(280, scroller.ScrollTop);
    }

    [Fact]
    public void ScrollMargin_ExtendsTheSnapArea()
    {
        var document = Parse(VerticalScroller(itemStyle: "scroll-margin-top: 10px"));
        var scroller = document.GetElementById("scroller")!;

        scroller.ScrollTop = 280;
        Assert.Equal(290, scroller.ScrollTop);
    }

    [Theory]
    [InlineData("vertical-lr", "block", "start", 300)]
    [InlineData("vertical-rl", "block", "start", -300)]
    [InlineData("vertical-rl", "block", "end", -200)]
    [InlineData("horizontal-tb; direction: rtl", "inline-block", "start", -300)]
    [InlineData("horizontal-tb; direction: rtl", "inline-block", "end", -200)]
    public void WritingMode_ResolvesTheAlignmentWithTheWritingModeOfTheScrollContainer(string writingMode, string display, string align, double expected)
    {
        // A horizontal scroller whose 100px wide snap areas are in its block axis (vertical writing modes) or its inline axis (rtl)
        var items = string.Concat(Enumerable.Range(0, 10).Select(_ => $"<div style='display: {display}; width: 100px; height: 100px; scroll-snap-align: {align}'></div>"));
        var document = Parse($"<div id=scroller style='writing-mode: {writingMode}; width: 100px; height: 100px; overflow: hidden; white-space: nowrap; line-height: 0; scroll-snap-type: x mandatory'>{items}</div>");
        var scroller = document.GetElementById("scroller")!;

        scroller.ScrollLeft = expected + (expected < 0 ? 30 : -30);
        Assert.Equal(expected, scroller.ScrollLeft);
    }

    [Fact]
    public void ScrollIntoView_SnapsToTheSnapPositionOfTheTarget()
    {
        var document = Parse(VerticalScroller(itemStyle: "scroll-snap-align: end"));
        var scroller = document.GetElementById("scroller")!;

        // The end alignment of the snap area wins over the block: start of scrollIntoView
        _ = document.GetElementById("item5")!.ScrollIntoView(new ScrollIntoViewOptions { Block = ScrollLogicalPosition.Start });
        Assert.Equal(500, scroller.ScrollTop);
    }

    [Fact]
    public void ScrollSnapType_OnTheRootElementAppliesToTheViewport()
    {
        var items = string.Concat(Enumerable.Range(0, 10).Select(_ => "<div style='height: 600px; scroll-snap-align: start'></div>"));
        var document = Parse("<style>html { scroll-snap-type: y mandatory; scrollbar-width: none }</style>" + items);
        var window = document.DefaultView!;

        _ = window.ScrollTo(new ScrollToOptions { Top = 1000 });
        Assert.Equal(1200, window.ScrollY);

        _ = window.ScrollBy(new ScrollToOptions { Top = 10 });
        Assert.Equal(1800, window.ScrollY);
    }

    [Fact]
    public void SnapEvents_AreFiredWhenTheSnapTargetsChange()
    {
        var document = Parse(VerticalScroller());
        var scroller = document.GetElementById("scroller")!;

        // The first snap does not fire snap events
        _ = scroller.ScrollTop;
        RunFrames();
        var events = new List<string>();
        foreach (var type in new[] { "scroll", "scrollend", "scrollsnapchanging", "scrollsnapchange" })
        {
            scroller.AddEventListener(type, e => events.Add(e is SnapEvent snap ? $"{e.Type}:{((Element?)snap.SnapTargetBlock)?.Id}:{((Element?)snap.SnapTargetInline)?.Id}" : e.Type));
        }

        scroller.ScrollTop = 190;
        RunFrames();
        Assert.Equal(["scrollsnapchanging:item2:", "scroll", "scrollsnapchange:item2:", "scrollend"], events);

        // The same target: no snap event
        events.Clear();
        scroller.ScrollTop = 210;
        RunFrames();
        Assert.Empty(events);

        // The snap container stops snapping
        scroller.SetAttribute("style", "width: 100px; height: 100px; overflow: hidden");
        _ = scroller.ScrollTop;
        RunFrames();
        Assert.Equal(["scrollsnapchanging::", "scrollsnapchange::"], events);
    }

    [Fact]
    public void SnapEvent_OfTheViewportBubblesFromTheDocument()
    {
        var items = string.Concat(Enumerable.Range(0, 10).Select(i => $"<div id=item{i} style='height: 600px; scroll-snap-align: start'></div>"));
        var document = Parse("<style>html { scroll-snap-type: y mandatory; scrollbar-width: none }</style>" + items);
        var window = document.DefaultView!;
        SnapEvent? snapEvent = null;
        window.AddEventListener("scrollsnapchange", e => snapEvent = (SnapEvent)e);

        _ = window.ScrollTo(new ScrollToOptions { Top = 1300 });
        RunFrames();
        Assert.NotNull(snapEvent);
        Assert.True(snapEvent.Bubbles);
        Assert.Same(document, snapEvent.Target);
        Assert.Same(document.GetElementById("item2"), snapEvent.SnapTargetBlock);
    }

    [Fact]
    public void ScrollIntoView_SnapsToTheTargetWhenItIsAlreadyVisible()
    {
        // https://drafts.csswg.org/css-scroll-snap-1/#choosing: a scroll intended to scroll to an element snaps to one of its snap
        // positions, even when the closest snap position is another one
        var items = string.Concat(Enumerable.Range(0, 6).Select(i => $"<div id=item{i} style='display: inline-block; width: 100px; height: 50px; scroll-snap-align: center'></div>"));
        var document = Parse($"<div id=scroller style='width: 400px; height: 50px; overflow: hidden; white-space: nowrap; line-height: 0; font-size: 0; scroll-snap-type: x mandatory'>{items}</div>");
        var scroller = document.GetElementById("scroller")!;
        Assert.Equal(0, scroller.ScrollLeft);

        _ = document.GetElementById("item2")!.ScrollIntoView();
        RunFrames(5);
        Assert.Equal(50, scroller.ScrollLeft);
    }

    [Fact]
    public void Focus_SnapsToTheClosestSnapPosition()
    {
        var items = string.Concat(Enumerable.Range(0, 6).Select(i => $"<div id=item{i} tabindex=0 style='display: inline-block; width: 100px; height: 50px; scroll-snap-align: center'></div>"));
        var document = Parse($"<div id=scroller style='width: 400px; height: 50px; overflow: hidden; white-space: nowrap; line-height: 0; font-size: 0; scroll-snap-type: x mandatory'>{items}</div>");
        var scroller = document.GetElementById("scroller")!;

        ((HtmlElement)document.GetElementById("item1")!).Focus();
        RunFrames(5);
        Assert.Equal(0, scroller.ScrollLeft);
    }

    [Fact]
    public void ColumnPseudoElements_AreSnapAreas()
    {
        // https://drafts.csswg.org/css-multicol-2/#column-pseudo: the columns of a multi-column scroll container are snap areas
        var document = Parse("""
            <style>#scroller::column { scroll-snap-align: start }</style>
            <div id=scroller style='width: 100px; height: 100px; overflow: hidden; columns: 1; gap: 0; column-fill: auto; scroll-snap-type: x mandatory'>
              <div style='height: 300px'></div>
            </div>
            """);
        var scroller = document.GetElementById("scroller")!;
        Assert.Equal(300, scroller.ScrollWidth);

        scroller.ScrollLeft = 140;
        Assert.Equal(100, scroller.ScrollLeft);
        scroller.ScrollLeft = 160;
        Assert.Equal(200, scroller.ScrollLeft);
    }

    [Fact]
    public void ScrollInitialTarget_SetsTheInitialScrollPosition()
    {
        var document = Parse(VerticalScroller(type: "none", itemStyle: "scroll-snap-align: none").Replace("id=item3 style='", "id=item3 style='scroll-initial-target: nearest; ", StringComparison.Ordinal));
        var scroller = document.GetElementById("scroller")!;
        Assert.Equal(300, scroller.ScrollTop);
    }

    [Fact]
    public void ScrollInitialTarget_AlignsWithScrollSnapAlign()
    {
        var document = Parse(VerticalScroller(type: "none").Replace("id=item3 style='", "id=item3 style='scroll-initial-target: nearest; scroll-snap-align: center !important; height: 50px !important; ", StringComparison.Ordinal));
        var scroller = document.GetElementById("scroller")!;
        Assert.Equal(275, scroller.ScrollTop);
    }

    [Fact]
    public void ScrollInitialTarget_IsSnapped()
    {
        // The initial scroll position (300) is snapped to the closest snap position (item4 at 350)
        var document = Parse(VerticalScroller().Replace("id=item3 style='", "id=item3 style='scroll-initial-target: nearest; scroll-snap-align: none !important; height: 50px !important; ", StringComparison.Ordinal));
        var scroller = document.GetElementById("scroller")!;
        Assert.Equal(350, scroller.ScrollTop);
    }

    [Fact]
    public void ScrollInitialTarget_IsFollowedUntilAScrollOperation()
    {
        var document = Parse(VerticalScroller(type: "none", itemStyle: "scroll-snap-align: none"));
        var scroller = document.GetElementById("scroller")!;
        Assert.Equal(0, scroller.ScrollTop);

        // A target that arrives after the first layout is scrolled to
        var item = document.GetElementById("item4")!;
        item.SetAttribute("style", "height: 100px; scroll-initial-target: nearest");
        Assert.Equal(400, scroller.ScrollTop);

        // The target moves
        document.GetElementById("item0")!.SetAttribute("style", "height: 150px");
        Assert.Equal(450, scroller.ScrollTop);

        // A scroll operation ends the initial scroll
        scroller.ScrollTop = 100;
        document.GetElementById("item0")!.SetAttribute("style", "height: 100px");
        Assert.Equal(100, scroller.ScrollTop);
    }
}
