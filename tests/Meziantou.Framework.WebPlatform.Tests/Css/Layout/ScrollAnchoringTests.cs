using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Html;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Layout;

/// <summary>
/// Scroll anchoring (https://drafts.csswg.org/css-scroll-anchoring/): the anchor node selection, the adjustment of the scroll
/// position after the layout changes, the suppression triggers, and the interactions with the scroll events, the layout shifts
/// and the scroll snapping.
/// </summary>
public sealed class ScrollAnchoringTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly FakeTimeProvider _clock = new();

    public void Dispose() => _contexts.Dispose();

    /// <summary>Parses a no-quirks document whose body has no margin, in a 800x600 viewport.</summary>
    private Document Parse(string html, string doctype = "<!DOCTYPE html>")
    {
        var context = _contexts.Create(new BrowsingContextOptions
        {
            TimeProvider = _clock,
            Viewport = new ViewportOptions { Width = 800, Height = 600 },
        });
        var document = context.Navigate("https://example.com/", _ => doctype + "<style>body { margin: 0 }</style>" + html);
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

    private static HtmlElement Get(Document document, string id) => (HtmlElement)document.GetElementById(id)!;

    [Fact]
    public void GrowthAboveTheViewport_AdjustsTheScrollPosition()
    {
        var document = Parse("<style>body { height: 4000px } div { height: 100px }</style><div id=block1>abc</div><div id=block2>def</div>");
        var scroller = document.ScrollingElement!;

        scroller.ScrollTop = 150;
        Get(document, "block1").Style.SetProperty("height", "200px");

        // The forced layout of the read performs the adjustment: #block2 stays in place
        Assert.Equal(250, scroller.ScrollTop);
    }

    [Fact]
    public void ScrollContainer_IsAdjustedByTheMovementOfItsAnchor()
    {
        var document = Parse("""
            <div id=scroller style='overflow: scroll; width: 200px; height: 200px'>
              <div id=space0></div><div id=a1 style='height: 200px'></div><div id=space1 style='height: 200px'></div>
              <div id=a2 style='height: 200px'></div><div style='height: 200px'></div>
            </div>
            """);
        var scroller = Get(document, "scroller");

        scroller.ScrollTop = 500;
        Get(document, "space1").Style.SetProperty("height", "300px");
        Assert.Equal(600, scroller.ScrollTop);

        // An explicit scroll selects a new anchor (#a1): moving #a2 does not adjust anymore
        scroller.ScrollTop = 100;
        Get(document, "space1").Style.SetProperty("height", "400px");
        Assert.Equal(100, scroller.ScrollTop);
        Get(document, "space0").Style.SetProperty("height", "100px");
        Assert.Equal(200, scroller.ScrollTop);
    }

    [Fact]
    public void ZeroScrollPosition_SelectsNoAnchor()
    {
        var document = Parse("<style>body { height: 4000px } div { height: 100px }</style><div id=block1></div><div id=block2></div>");
        Get(document, "block1").Style.SetProperty("height", "200px");
        Assert.Equal(0, document.ScrollingElement!.ScrollTop);
    }

    [Fact]
    public void ChangeOfAPositioningPropertyOnTheAnchorPath_SuppressesTheAdjustment()
    {
        var document = Parse("""
            <style>body { height: 4000px } #before, #anchor { height: 100px }</style>
            <div id=ancestor style='position: relative'><div id=before></div><div id=anchor></div></div>
            """);
        var scroller = document.ScrollingElement!;

        scroller.ScrollTop = 150;
        Get(document, "ancestor").Style.SetProperty("padding-top", "20px");
        Get(document, "before").Style.SetProperty("height", "150px");
        Assert.Equal(150, scroller.ScrollTop);

        // The suppression invalidated the anchor; a change of a property that does not affect the position is not a trigger
        scroller.ScrollTop = 200;
        Get(document, "ancestor").Style.SetProperty("color", "red");
        Get(document, "before").Style.SetProperty("height", "200px");
        Assert.Equal(250, scroller.ScrollTop);
    }

    [Fact]
    public void ElementBecomingAbsolutelyPositioned_SuppressesTheAdjustment()
    {
        var document = Parse("""
            <style>body { height: 4000px } #header { height: 50px } #content { height: 400px }</style>
            <div id=header></div><div id=before></div><div id=content></div>
            """);
        var scroller = document.ScrollingElement!;

        scroller.ScrollTop = 200;
        Get(document, "header").Style.SetProperty("position", "absolute");
        Get(document, "before").Style.SetProperty("height", "25px");
        Assert.Equal(200, scroller.ScrollTop);

        scroller.ScrollTop = 300;
        Get(document, "header").Style.SetProperty("position", "relative");
        Get(document, "before").Style.SetProperty("height", "50px");
        Assert.Equal(300, scroller.ScrollTop);

        scroller.ScrollTop = 200;
        Get(document, "header").Style.SetProperty("top", "auto");
        Get(document, "before").Style.SetProperty("height", "75px");
        Assert.Equal(225, scroller.ScrollTop);
    }

    [Fact]
    public void AbsolutelyPositionedElementBecomingDisplayNone_DoesNotSuppressTheAdjustment()
    {
        var document = Parse("""
            <style>body { height: 4000px } #before, #content { height: 100px } #abspos { position: absolute; height: 50px }</style>
            <div id=abspos></div><div id=before></div><div id=content></div>
            """);
        var scroller = document.ScrollingElement!;

        // The box of the element is removed: its position does not change (as Blink, which destroys its layout object)
        scroller.ScrollTop = 150;
        Get(document, "abspos").Style.SetProperty("display", "none");
        Get(document, "abspos").Style.SetProperty("position", "static");
        Get(document, "before").Style.SetProperty("height", "120px");
        Assert.Equal(170, scroller.ScrollTop);
    }

    [Fact]
    public void OverflowAnchorNone_ExcludesTheSubtree()
    {
        var document = Parse("""
            <style>body { height: 4000px } #A, #B { width: 100px } #B { height: 100px } #inner { height: 100px }</style>
            <div id=changer1></div><div id=A style='overflow-anchor: none'><div id=inner></div><div id=changer2></div></div><div id=B></div>
            """);
        var scroller = document.ScrollingElement!;

        scroller.ScrollTop = 50;
        Get(document, "changer1").Style.SetProperty("height", "100px");
        Get(document, "changer2").Style.SetProperty("height", "50px");
        Assert.Equal(200, scroller.ScrollTop);

        // A dynamic change of overflow-anchor on the anchor invalidates it
        Get(document, "B").Style.SetProperty("overflow-anchor", "none");
        Get(document, "changer2").Style.SetProperty("height", "100px");
        Assert.Equal(200, scroller.ScrollTop);
    }

    [Fact]
    public void OverflowAnchorNoneOnTheScroller_DisablesTheAnchoring()
    {
        var document = Parse("""
            <div id=scroller style='overflow: scroll; width: 200px; height: 200px; overflow-anchor: none'>
              <div id=changer></div><div style='height: 1000px'></div>
            </div>
            """);
        var scroller = Get(document, "scroller");

        scroller.ScrollTop = 100;
        Get(document, "changer").Style.SetProperty("height", "100px");
        Assert.Equal(100, scroller.ScrollTop);
    }

    [Fact]
    public void FixedPositionedElement_IsNotAnAnchor()
    {
        var document = Parse("""
            <style>body { height: 4000px }</style>
            <div id=changer></div><div style='position: fixed; top: 0; width: 100px; height: 100px'></div>
            <div style='height: 1000px'></div>
            """);
        var scroller = document.ScrollingElement!;

        scroller.ScrollTop = 100;
        Get(document, "changer").Style.SetProperty("height", "50px");
        Assert.Equal(150, scroller.ScrollTop);
    }

    [Fact]
    public void TextNode_CanBeTheAnchor()
    {
        var document = Parse("""
            <style>body { height: 4000px; font: 20px/1 Ahem } </style>
            <div id=changer></div><div style='height: 100px'></div><span>a<br>b<br>c<br>d</span>
            """);
        var scroller = document.ScrollingElement!;

        scroller.ScrollTop = 110;
        Get(document, "changer").Style.SetProperty("height", "30px");
        Assert.Equal(140, scroller.ScrollTop);
    }

    [Fact]
    public void VerticalRl_AdjustsTheHorizontalScrollPosition()
    {
        var document = Parse("""
            <style>html { writing-mode: vertical-rl } body { width: 4000px } div { width: 100px }</style>
            <div id=block1></div><div id=block2></div>
            """);
        var scroller = document.ScrollingElement!;

        scroller.ScrollLeft = -150;
        Get(document, "block1").Style.SetProperty("width", "200px");
        Assert.Equal(-250, scroller.ScrollLeft);
        Assert.Equal(0, scroller.ScrollTop);
    }

    [Fact]
    public void NestedScrollers_AreAnchoredIndependently()
    {
        var document = Parse("""
            <style>body { height: 2000px } .anchor { position: relative; height: 100px; width: 150px }</style>
            <div id=scroller style='overflow: scroll; width: 500px; height: 300px'>
              <div id=innerChanger></div><div class=anchor></div><div style='height: 500px'></div>
            </div>
            <div id=outerChanger></div><div class=anchor></div>
            """);
        var scroller = Get(document, "scroller");

        scroller.ScrollTop = 100;
        document.ScrollingElement!.ScrollTop = 350;
        Get(document, "innerChanger").Style.SetProperty("height", "200px");
        Get(document, "outerChanger").Style.SetProperty("height", "150px");
        Assert.Equal(300, scroller.ScrollTop);
        Assert.Equal(500, document.ScrollingElement.ScrollTop);
    }

    [Fact]
    public void FocusedEditableElement_IsThePriorityCandidate()
    {
        var document = Parse("""
            <style>body { height: 4000px } .spacer { height: 100px } #growing { height: 100px } #focused { height: 10px }</style>
            <div class=spacer></div><div class=spacer></div><div class=spacer></div><div class=spacer></div>
            <div id=growing></div><div class=spacer></div><div id=focused contenteditable></div><div class=spacer></div>
            """);
        var scroller = document.ScrollingElement!;

        scroller.ScrollTop = 150;
        Get(document, "focused").Focus();
        var top = Get(document, "focused").GetBoundingClientRect().Top;
        var scrollTop = scroller.ScrollTop;

        // Without the priority, the anchor would be the second spacer, which does not move
        Get(document, "growing").Style.SetProperty("height", "3000px");
        Assert.Equal(top, Get(document, "focused").GetBoundingClientRect().Top);
        Assert.Equal(scrollTop + 2900, scroller.ScrollTop);
    }

    [Fact]
    public void Adjustment_FiresAScrollEventAtTheNextRenderingUpdate()
    {
        var document = Parse("<style>body { height: 4000px } div { height: 100px }</style><div id=block1></div><div id=block2></div>");
        var window = document.DefaultView!;
        var scroller = document.ScrollingElement!;
        scroller.ScrollTop = 150;
        RunFrames();
        var events = new List<string>();
        window.AddEventListener("scroll", e => events.Add(e.Type));
        window.AddEventListener("scrollend", e => events.Add(e.Type));

        Get(document, "block1").Style.SetProperty("height", "200px");
        Assert.Equal(250, scroller.ScrollTop);
        RunFrames(2);
        Assert.Equal(["scroll", "scrollend"], events);

        // Adjustments that cancel each other out before the rendering update fire no event
        events.Clear();
        Get(document, "block1").Style.SetProperty("height", "300px");
        Assert.Equal(350, scroller.ScrollTop);
        Get(document, "block1").Style.SetProperty("height", "200px");
        Assert.Equal(250, scroller.ScrollTop);
        RunFrames(2);
        Assert.Empty(events);
    }

    [Fact]
    public void Adjustment_IsNotALayoutShift()
    {
        var document = Parse("""
            <style>body { height: 4000px } div { height: 100px; background: blue }</style>
            <div id=block1></div><div id=block2></div><div id=block3></div>
            """);
        var entries = new List<PerformanceEntry>();
        var observer = new PerformanceObserver(document.DefaultView!, (list, _, _) => entries.AddRange(list.GetEntries()));
        observer.Observe(new PerformanceObserverInit { Type = "layout-shift" });
        document.ScrollingElement!.ScrollTop = 150;
        RunFrames(2);

        Get(document, "block1").Style.SetProperty("height", "200px");
        RunFrames(2);
        Assert.Equal(250, document.ScrollingElement.ScrollTop);
        Assert.Empty(entries);
    }

    [Fact]
    public void SnappedScrollContainer_ReSnapsAfterTheAdjustment()
    {
        var document = Parse("""
            <div id=scroller style='overflow: hidden; width: 100px; height: 100px; scroll-snap-type: y mandatory'>
              <div id=changer style='height: 100px'></div>
              <div id=target style='height: 100px; scroll-snap-align: start'></div>
              <div style='height: 50px'></div>
              <div id=other style='height: 100px; scroll-snap-align: start'></div>
              <div style='height: 500px'></div>
            </div>
            """);
        var scroller = Get(document, "scroller");
        scroller.ScrollTop = 100;
        Assert.Equal(100, scroller.ScrollTop);

        // The anchor (#target) moves by 30px: the snap container follows its snap target
        Get(document, "changer").Style.SetProperty("height", "130px");
        Assert.Equal(130, scroller.ScrollTop);
    }

    [Fact]
    public void FragmentNavigation_AnchorsTheTarget()
    {
        var document = Parse("""
            <style>body { height: 2000px } #first { height: 1000px } #anchor { position: absolute; height: 600px; width: 100% }
            #fragment { position: relative; height: 200px; width: 200px; margin: 10px }</style>
            <div id=first></div><div id=changer></div><div id=anchor><div id=fragment></div></div>
            """, doctype: "");
        var window = document.DefaultView!;
        window.Location.Hash = "fragment";
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal(1010, window.ScrollY);

        Get(document, "changer").Style.SetProperty("height", "100px");
        Assert.Equal(1110, window.ScrollY);
    }
}
