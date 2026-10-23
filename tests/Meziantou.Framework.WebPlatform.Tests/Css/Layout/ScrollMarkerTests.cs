using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Dom;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Layout;

/// <summary>
/// The scroll markers and the scroll buttons of CSS Overflow 5 (https://drafts.csswg.org/css-overflow-5/#scroll-navigation):
/// the boxes of the pseudo-elements, the active marker, the state of the buttons and their activation.
/// </summary>
public sealed class ScrollMarkerTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly FakeTimeProvider _clock = new();

    public void Dispose() => _contexts.Dispose();

    /// <summary>Parses a no-quirks document whose body has no margin, in a 800x600 viewport.</summary>
    private Document Parse(string html)
    {
        var context = _contexts.Create(new BrowsingContextOptions { TimeProvider = _clock, Viewport = new ViewportOptions { Width = 800, Height = 600 } });
        var document = context.Navigate("https://example.com/", _ => "<!DOCTYPE html><style>body { margin: 0 }</style>" + html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    /// <summary>Runs the rendering steps of the next frames.</summary>
    private void RunFrames(int count = 2)
    {
        for (var i = 0; i < count; i++)
        {
            _clock.Advance(TimeSpan.FromMilliseconds(17));
            _contexts.EventLoop.RunUntilIdle();
        }
    }

    private static Element Get(Document document, string id) => document.GetElementById(id)!;

    private static string GetStyle(Document document, string id, string pseudoElement, string property)
        => document.DefaultView!.GetComputedStyle(Get(document, id), pseudoElement).GetPropertyValue(property);

    private static PhysicalRect GetRect(Document document, string id, string pseudoElement)
        => document.GetLayoutEngine().GetFragments(Get(document, id), pseudoElement)[0].GetRectInRoot();

    /// <summary>The trusted events of a click of the primary button at a point of the viewport, dispatched to the element at that point.</summary>
    private static void Click(Document document, int x, int y)
    {
        var target = document.ElementFromPoint(x, y)!;
        foreach (var type in (ReadOnlySpan<string>)["mousedown", "mouseup", "click"])
        {
            var init = new PointerEventInit { Bubbles = true, Cancelable = true, Composed = true, ClientX = x, ClientY = y, Button = 0, Buttons = (ushort)(type == "mousedown" ? 1 : 0), View = document.DefaultView };
            Event @event = type == "click" ? new PointerEvent(type, init) : new MouseEvent(type, init);
            @event.IsTrusted = true;
            EventDispatcher.Dispatch(@event, target);
        }
    }

    private static void PressKey(Document document, string key)
    {
        var target = document.ActiveElement ?? document.Body!;
        EventDispatcher.Dispatch(new KeyboardEvent("keydown", new KeyboardEventInit { Key = key, Bubbles = true, Cancelable = true, Composed = true }) { IsTrusted = true }, target);
        EventDispatcher.Dispatch(new KeyboardEvent("keyup", new KeyboardEventInit { Key = key, Bubbles = true, Cancelable = true, Composed = true }) { IsTrusted = true }, document.ActiveElement ?? document.Body!);
    }

    // A 200x100 scroll container with three 100px high items, whose markers are 20x20 boxes in a flex group
    private const string Carousel = """
        <style>
          #scroller { width: 200px; height: 100px; overflow: hidden; scroll-marker-group: before; }
          #scroller::scroll-marker-group { display: flex; height: 20px; width: 200px; }
          #scroller > div { height: 100px; }
          #scroller > div::scroll-marker { content: ""; width: 20px; height: 20px; background-color: red; }
          #scroller > div::scroll-marker:target-current { background-color: green; }
          #scroller > div::scroll-marker:focus { opacity: 0.5; }
        </style>
        <div id=scroller><div id=a></div><div id=b></div><div id=c></div></div>
        """;

    [Fact]
    public void Group_IsBeforeTheScroller_WithTheMarkersInTreeOrder()
    {
        var document = Parse(Carousel);
        Assert.Equal(new PhysicalRect(0, 0, 200, 20), GetRect(document, "scroller", "scroll-marker-group"));
        Assert.Equal(new PhysicalRect(0, 20, 200, 100), document.GetLayoutEngine().GetFragments(Get(document, "scroller"))[0].GetRectInRoot());
        Assert.Equal(new PhysicalRect(0, 0, 20, 20), GetRect(document, "a", "scroll-marker"));
        Assert.Equal(new PhysicalRect(20, 0, 20, 20), GetRect(document, "b", "scroll-marker"));
        Assert.Equal(new PhysicalRect(40, 0, 20, 20), GetRect(document, "c", "scroll-marker"));
    }

    [Fact]
    public void Group_After_FollowsTheScrollerAndItsButtons()
    {
        var document = Parse("""
            <style>
              #scroller { width: 200px; height: 100px; overflow: hidden; scroll-marker-group: after; }
              #scroller::scroll-marker-group { height: 20px; }
              #scroller::scroll-button(inline-end) { content: ">"; display: block; height: 30px; border: none; padding: 0; }
              #scroller > div { height: 300px; width: 600px; }
            </style>
            <div id=scroller><div></div></div>
            """);
        Assert.Equal(100, GetRect(document, "scroller", "scroll-button(inline-end)").Y);
        Assert.Equal(130, GetRect(document, "scroller", "scroll-marker-group").Y);
    }

    [Fact]
    public void Group_IsNotGeneratedWithoutScrollContainer()
    {
        var document = Parse("""
            <style>
              #scroller { scroll-marker-group: before; }
              #scroller::scroll-marker-group { height: 20px; }
              #a::scroll-marker { content: "x"; }
            </style>
            <div id=scroller><div id=a></div></div>
            """);
        Assert.Empty(document.GetLayoutEngine().GetFragments(Get(document, "scroller"), "scroll-marker-group"));

        // The declarations of a marker that is not generated are empty
        Assert.Equal("", GetStyle(document, "a", "::scroll-marker", "content"));
    }

    [Fact]
    public void Marker_OfAnAbsolutelyPositionedBoxOutsideOfTheScroller_IsNotGenerated()
    {
        // The containing block of #b is the initial containing block: the scroll container does not scroll it
        var document = Parse(Carousel + "<style>#b { position: absolute }</style>");
        Assert.Single(document.GetLayoutEngine().GetFragments(Get(document, "a"), "scroll-marker"));
        Assert.Empty(document.GetLayoutEngine().GetFragments(Get(document, "b"), "scroll-marker"));
    }

    [Fact]
    public void Group_HasSizeAndLayoutContainment()
    {
        var document = Parse(Carousel + "<style>#scroller::scroll-marker-group { height: auto; contain: none }</style>");
        Assert.Equal("size layout", GetStyle(document, "scroller", "::scroll-marker-group", "contain"));
        Assert.Equal(0, GetRect(document, "scroller", "scroll-marker-group").Height);
    }

    [Fact]
    public void TargetCurrent_FollowsTheScrollPosition()
    {
        var document = Parse(Carousel);
        Assert.Equal("rgb(0, 128, 0)", GetStyle(document, "a", "::scroll-marker", "background-color"));
        Assert.Equal("rgb(255, 0, 0)", GetStyle(document, "b", "::scroll-marker", "background-color"));

        // The active marker is a snapshot taken by the rendering steps
        Get(document, "scroller").ScrollTop = 100;
        Assert.Equal("rgb(0, 128, 0)", GetStyle(document, "a", "::scroll-marker", "background-color"));
        RunFrames();
        Assert.Equal("rgb(255, 0, 0)", GetStyle(document, "a", "::scroll-marker", "background-color"));
        Assert.Equal("rgb(0, 128, 0)", GetStyle(document, "b", "::scroll-marker", "background-color"));
    }

    [Fact]
    public void TargetCurrent_OfTheLinksOfAScrollTargetGroup()
    {
        var document = Parse("""
            <style>
              nav { scroll-target-group: auto; }
              #scroller { width: 200px; height: 100px; overflow: hidden; }
              #scroller > div { height: 100px; }
            </style>
            <nav><a id=l1 href="#a">a</a><a id=l2 href="#b">b</a><a id=l3 href="#c">c</a><a id=other href="https://example.org/#a">x</a></nav>
            <div id=scroller><div id=a></div><div id=b></div><div id=c></div></div>
            """);
        document.GetLayoutEngine().EnsureLayout();
        Assert.True(Get(document, "l1").Matches(":target-current"));
        Assert.True(Get(document, "l2").Matches(":target-after"));
        Assert.False(Get(document, "other").Matches(":target-after"));

        Get(document, "scroller").ScrollTop = 100;
        RunFrames();
        Assert.True(Get(document, "l1").Matches(":target-before"));
        Assert.True(Get(document, "l2").Matches(":target-current"));
        Assert.True(Get(document, "l3").Matches(":target-after"));
    }

    [Fact]
    public void Click_OnAMarker_ScrollsItsOriginatingElementIntoView()
    {
        var document = Parse(Carousel);
        var scroller = Get(document, "scroller");
        var clicked = new List<EventTarget?>();
        document.AddEventListener("click", e => clicked.Add(e.Target));

        // The third marker; the target of the click is its originating element
        Assert.Same(Get(document, "c"), document.ElementFromPoint(50, 10));
        Click(document, 50, 10);
        Assert.Equal(200, scroller.ScrollTop);
        Assert.Equal([Get(document, "c")], clicked);
        RunFrames();
        Assert.Equal("rgb(0, 128, 0)", GetStyle(document, "c", "::scroll-marker", "background-color"));

        // In the links mode, the marker does not keep the focus
        Assert.Same(document.Body, document.ActiveElement);
        Assert.Equal("1", GetStyle(document, "c", "::scroll-marker", "opacity"));
    }

    [Fact]
    public void TabsMode_TheMarkerKeepsTheFocus_AndTheArrowKeysActivateTheOtherMarkers()
    {
        var document = Parse(Carousel + "<style>#scroller { scroll-marker-group: before tabs }</style>");
        var scroller = Get(document, "scroller");
        Click(document, 10, 10);
        RunFrames();

        // The focused area is the scroll container, which does not match :focus: the marker does
        Assert.Same(scroller, document.ActiveElement);
        Assert.False(scroller.Matches(":focus"));
        Assert.True(scroller.Matches(":focus-within"));
        Assert.Equal("0.5", GetStyle(document, "a", "::scroll-marker", "opacity"));

        PressKey(document, "ArrowRight");
        RunFrames();
        Assert.Equal(100, scroller.ScrollTop);
        Assert.Equal("0.5", GetStyle(document, "b", "::scroll-marker", "opacity"));
        Assert.Equal("rgb(0, 128, 0)", GetStyle(document, "b", "::scroll-marker", "background-color"));

        // The navigation cycles
        PressKey(document, "ArrowLeft");
        PressKey(document, "ArrowLeft");
        RunFrames();
        Assert.Equal(200, scroller.ScrollTop);
        Assert.Equal("0.5", GetStyle(document, "c", "::scroll-marker", "opacity"));
    }

    [Fact]
    public void Tab_FocusesTheMarkersBeforeTheContentsOfTheScroller()
    {
        var document = Parse(Carousel + "<style>#scroller > div::scroll-marker:focus-visible { outline: 1px solid blue }</style><button id=after></button>");
        Get(document, "a").SetAttribute("tabindex", "0");
        PressKey(document, "Tab");
        Assert.Same(Get(document, "scroller"), document.ActiveElement);
        Assert.Equal("0.5", GetStyle(document, "a", "::scroll-marker", "opacity"));
        Assert.Equal("solid", GetStyle(document, "a", "::scroll-marker", "outline-style"));

        PressKey(document, "Tab");
        PressKey(document, "Tab");
        Assert.Equal("0.5", GetStyle(document, "c", "::scroll-marker", "opacity"));
        PressKey(document, "Tab");
        Assert.Same(Get(document, "a"), document.ActiveElement);
        PressKey(document, "Tab");
        Assert.Same(Get(document, "after"), document.ActiveElement);
    }

    [Fact]
    public void ColumnMarkers_OnePerColumn_ScrollTheirColumnIntoView()
    {
        // 450px of content in columns of 100x100: five columns, three in the scrollport
        var document = Parse("""
            <style>
              #scroller { width: 300px; height: 100px; overflow: hidden; columns: 3; column-gap: 0; column-fill: auto; scroll-marker-group: before; }
              #scroller::scroll-marker-group { display: flex; height: 20px; width: 300px; }
              #scroller::column::scroll-marker { content: ""; width: 20px; height: 20px; background-color: red; }
              #scroller::column::scroll-marker:target-current { background-color: green; }
            </style>
            <div id=scroller><div style="height: 450px"></div></div>
            """);
        var scroller = Get(document, "scroller");
        Assert.Equal(new PhysicalRect(80, 0, 20, 20), GetRect(document, "scroller", "column::scroll-marker(4)"));
        Assert.Empty(document.GetLayoutEngine().GetFragments(scroller, "column::scroll-marker(5)"));

        // The fourth marker scrolls its column to the start of the scrollport, as far as the scroll range allows
        Click(document, 70, 10);
        Assert.Equal(200, scroller.ScrollLeft);
        Click(document, 30, 10);
        Assert.Equal(100, scroller.ScrollLeft);

        // A column less: a marker less
        ((HtmlElement)scroller.FirstElementChild!).Style.SetProperty("height", "350px");
        Assert.Single(document.GetLayoutEngine().GetFragments(scroller, "column::scroll-marker(3)"));
        Assert.Empty(document.GetLayoutEngine().GetFragments(scroller, "column::scroll-marker(4)"));
    }

    [Fact]
    public void ColumnMarkers_OfAFieldset_ScrollTheColumnsOfItsContentBox()
    {
        var document = Parse("""
            <style>
              #scroller { width: 300px; height: 100px; overflow: hidden; columns: 3; column-gap: 0; column-fill: auto; scroll-marker-group: before; margin: 0; border: none; padding: 0; }
              #scroller::scroll-marker-group { display: flex; height: 20px; width: 300px; }
              #scroller::column::scroll-marker { content: ""; width: 20px; height: 20px; }
            </style>
            <fieldset id=scroller><div style="height: 450px"></div></fieldset>
            """);
        var scroller = Get(document, "scroller");
        Click(document, 30, 10);
        Assert.Equal(100, scroller.ScrollLeft);
        Click(document, 70, 10);
        Assert.Equal(200, scroller.ScrollLeft);
    }

    [Fact]
    public void ColumnMarker_OfAColumnWithoutElement_TheNavigationContinuesAfterTheMultiColumnContainer()
    {
        var document = Parse("""
            <style>
              #scroller { width: 300px; height: 100px; overflow: hidden; scroll-marker-group: after; }
              #scroller::scroll-marker-group { display: flex; height: 20px; width: 300px; }
              #multicol { columns: 3; column-gap: 0; column-fill: auto; height: 100%; }
              #multicol::column::scroll-marker { content: ""; width: 20px; height: 20px; }
            </style>
            <div id=scroller><div id=multicol><div id=first tabindex=0 style="height: 50px"></div><div style="height: 200px"></div></div></div>
            <div id=after tabindex=0></div>
            """);
        Click(document, 10, 110);
        PressKey(document, "Tab");
        Assert.Same(Get(document, "first"), document.ActiveElement);

        // The third column has no element: the next area is the one after the scroll container, not the markers of its group
        Click(document, 50, 110);
        PressKey(document, "Tab");
        Assert.Same(Get(document, "after"), document.ActiveElement);
    }

    [Theory]
    [InlineData("<a href=#nav id=originating class=scroller><span></span></a>", "")]
    [InlineData("<a href=#nav><div id=originating class=scroller><span></span></div></a>", "")]
    [InlineData("<a href=#nav id=originating></a>", "#nav")]
    [InlineData("<a href=#nav><div id=originating></div></a>", "#nav")]
    public void Click_OnAButtonInALink_FollowsTheLinkOnlyWhenTheButtonCannotScroll(string markup, string expectedHash)
    {
        var document = Parse("""
            <style>
              body { padding-top: 100px; }
              a { display: block; }
              #originating { width: 100px; height: 50px; white-space: nowrap; }
              .scroller { overflow: auto; }
              .scroller span { display: inline-block; width: 300px; height: 10px; }
              #originating::scroll-button(inline-end) { content: ""; position: absolute; top: 0; left: 0; width: 20px; height: 20px; }
            </style>
            """ + markup);
        Click(document, 10, 10);
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal(expectedHash, document.DefaultView!.Location.Hash);
    }

    // A 200x100 scroll container whose contents are 500px wide, with the buttons of the inline axis (20x20, below the scroller)
    private const string Buttons = """
        <style>
          #scroller { width: 200px; height: 100px; overflow: hidden; }
          #scroller > div { width: 500px; height: 100px; }
          #scroller::scroll-button(*) { display: block; width: 20px; height: 20px; border: none; padding: 0; background-color: red; }
          #scroller::scroll-button(left) { content: "<"; }
          #scroller::scroll-button(inline-end) { content: ">"; }
          #scroller::scroll-button(*):enabled { background-color: green; }
          #scroller::scroll-button(*):disabled { background-color: gray; }
        </style>
        <div id=scroller><div></div></div>
        """;

    [Fact]
    public void Buttons_AreDisabledWhenTheScrollerCannotScrollInTheirDirection()
    {
        var document = Parse(Buttons);

        // The physical direction designates the button of the flow-relative direction
        Assert.Equal("rgb(128, 128, 128)", GetStyle(document, "scroller", "::scroll-button(inline-start)", "background-color"));
        Assert.Equal("rgb(128, 128, 128)", GetStyle(document, "scroller", "::scroll-button(LEFT)", "background-color"));
        Assert.Equal("rgb(0, 128, 0)", GetStyle(document, "scroller", "::scroll-button(right)", "background-color"));

        Get(document, "scroller").ScrollLeft = 300;
        RunFrames();
        Assert.Equal("rgb(0, 128, 0)", GetStyle(document, "scroller", "::scroll-button(left)", "background-color"));
        Assert.Equal("rgb(128, 128, 128)", GetStyle(document, "scroller", "::scroll-button(right)", "background-color"));
    }

    [Fact]
    public void Click_OnAButton_ScrollsByOnePage_AndFocusesTheButton()
    {
        var document = Parse(Buttons);
        var scroller = Get(document, "scroller");

        // The inline-end button follows the inline-start one, below the scroller; the events target the scroller
        Assert.Equal(new PhysicalRect(0, 120, 20, 20), GetRect(document, "scroller", "scroll-button(inline-end)"));
        Assert.Same(scroller, document.ElementFromPoint(10, 130));
        Click(document, 10, 130);

        // 87.5% of the scrollport
        Assert.Equal(175, scroller.ScrollLeft);
        Assert.Same(scroller, document.ActiveElement);
        Assert.False(scroller.Matches(":focus"));

        // A click on a disabled button does nothing
        scroller.ScrollLeft = 0;
        RunFrames();
        Click(document, 10, 110);
        Assert.Equal(0, scroller.ScrollLeft);
    }
}
