using Meziantou.Framework.WebPlatform.Dom;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Html;

/// <summary>The scrolls of the user: the smooth scrolls of the wheel, the gestures and their scrollend event, the wheel event transactions.</summary>
public sealed class UserScrollingTests : IDisposable
{
    private const string Style = "<style>body { margin: 0 } #scroller { width: 100px; height: 100px; overflow: auto } #content { width: 100px; height: 1000px }</style>";

    private readonly BrowsingContextScope _contexts = new();
    private readonly FakeTimeProvider _clock = new();

    public void Dispose() => _contexts.Dispose();

    private Document Load(string html, bool smooth = false)
    {
        var context = _contexts.Create(new BrowsingContextOptions { TimeProvider = _clock, Viewport = new ViewportOptions { SmoothUserScrolling = smooth } });
        var document = context.Navigate("https://example.com/", _ => "<!DOCTYPE html>" + Style + html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    /// <summary>Runs the rendering steps of the next frames (the virtual clock advances by one frame each time).</summary>
    private void RunFrames(int count = 1)
    {
        for (var i = 0; i < count; i++)
        {
            _clock.Advance(TimeSpan.FromMilliseconds(17));
            _contexts.EventLoop.RunUntilIdle();
        }
    }

    private static List<string> Log(EventTarget target, params string[] types)
    {
        var events = new List<string>();
        foreach (var type in types)
        {
            target.AddEventListener(type, e => events.Add(e.Type));
        }

        return events;
    }

    [Fact]
    public void Wheel_GestureEndsWhenTheWheelStops()
    {
        var document = Load("<div id=scroller><div id=content></div></div>", smooth: true);
        var scroller = document.GetElementById("scroller")!;
        var events = Log(scroller, "scroll", "scrollend");
        var input = document.BrowsingContext!.Input;
        Assert.True(input.Wheel(50, 50, 0, 40));
        Assert.Equal(40, scroller.ScrollTop);
        RunFrames(3);
        Assert.True(input.Wheel(50, 50, 0, 40));
        Assert.Equal(80, scroller.ScrollTop);
        RunFrames(3);
        Assert.Equal(["scroll", "scroll"], events);

        // 100 ms without wheel event
        RunFrames(5);
        Assert.Equal(["scroll", "scroll", "scrollend"], events);
    }

    [Fact]
    public void Keys_SmoothScroll()
    {
        var document = Load("<div id=scroller tabindex=0><div id=content></div></div>", smooth: true);
        var scroller = (HtmlElement)document.GetElementById("scroller")!;
        var events = Log(scroller, "scrollend");
        scroller.Focus();
        EventDispatcher.Dispatch(new KeyboardEvent("keydown", new KeyboardEventInit { Key = "ArrowDown", Bubbles = true, Cancelable = true, Composed = true }) { IsTrusted = true }, scroller);
        Assert.Equal(0, scroller.ScrollTop);
        RunFrames(3);
        Assert.InRange(scroller.ScrollTop, 0.001, 39.999);

        // A key pressed during the animation adds its scroll to the destination
        EventDispatcher.Dispatch(new KeyboardEvent("keydown", new KeyboardEventInit { Key = "ArrowDown", Bubbles = true, Cancelable = true, Composed = true }) { IsTrusted = true }, scroller);
        RunFrames(30);
        Assert.Equal(80, scroller.ScrollTop);
        Assert.Equal(["scrollend"], events);
    }

    [Fact]
    public void Wheel_InstantScrollByDefault()
    {
        var document = Load("<div id=scroller><div id=content></div></div>");
        var scroller = document.GetElementById("scroller")!;
        var events = Log(scroller, "scrollend");
        Assert.True(document.BrowsingContext!.Input.Wheel(50, 50, 0, 40));
        Assert.Equal(40, scroller.ScrollTop);
        RunFrames();
        Assert.Equal(["scrollend"], events);
    }

    [Theory]
    [InlineData(10, 0)]
    [InlineData(60, 100)]
    [InlineData(140, 100)]
    public void Wheel_SnapsWhenTheGestureEnds(double delta, double expected)
    {
        var document = Load("<div id=scroller style='scroll-snap-type: y mandatory'><div style='height: 100px; scroll-snap-align: start'></div><div style='height: 100px; scroll-snap-align: start'></div><div style='height: 800px'></div></div>", smooth: true);
        var scroller = document.GetElementById("scroller")!;
        _ = scroller.ScrollTop;
        var events = Log(scroller, "scrollend", "scrollsnapchange");
        document.BrowsingContext!.Input.Wheel(50, 50, 0, delta);
        RunFrames(40);
        Assert.Equal(expected, scroller.ScrollTop);
        Assert.Equal(expected == 0 ? ["scrollend"] : ["scrollsnapchange", "scrollend"], events);
    }

    [Fact]
    public void Touch_PanFiresScrollEndWhenTheTouchIsReleased()
    {
        var document = Load("<div id=scroller><div id=content></div></div>");
        var scroller = document.GetElementById("scroller")!;
        var events = Log(scroller, "scrollend");
        var finger = document.BrowsingContext!.Input.CreatePointer("touch");
        finger.Move(50, 80);
        finger.Down();
        finger.Move(50, 60);
        RunFrames();
        finger.Move(50, 30);
        RunFrames();
        Assert.Equal(50, scroller.ScrollTop);
        Assert.Empty(events);

        finger.Up();
        RunFrames();
        Assert.Equal(["scrollend"], events);
    }

    [Fact]
    public void Wheel_TransactionKeepsTheTargetOfItsFirstEvent()
    {
        var document = Load("<div id=a style='height: 30px'></div><div id=b style='height: 2000px'></div>");
        var targets = new List<string?>();
        document.AddEventListener("wheel", e => targets.Add((e.Target as Element)?.Id));
        var input = document.BrowsingContext!.Input;
        input.Wheel(10, 10, 0, 50);
        input.Wheel(10, 10, 0, 50);
        Assert.Equal(["a", "a"], targets);

        // A move of the mouse ends the transaction: the content under the pointer was scrolled
        input.Mouse.Move(10, 11);
        input.Wheel(10, 11, 0, 50);
        Assert.Equal(["a", "a", "b"], targets);

        // The transaction ends after a delay
        _clock.Advance(TimeSpan.FromSeconds(1));
        input.Wheel(10, 11, 0, -150);
        _clock.Advance(TimeSpan.FromSeconds(1));
        input.Wheel(10, 11, 0, 0);
        Assert.Equal(["a", "a", "b", "b", "a"], targets);
    }

    [Fact]
    public void Wheel_TransactionEndsWhenItsTargetIsRemoved()
    {
        var document = Load("<div id=a style='height: 30px'></div><div id=b style='height: 2000px'></div>");
        var a = document.GetElementById("a")!;
        var targets = new List<string?>();
        document.AddEventListener("wheel", e => targets.Add((e.Target as Element)?.Id));
        var input = document.BrowsingContext!.Input;
        input.Wheel(10, 10, 0, 0);
        a.Remove();
        document.Body!.Prepend(a);
        input.Wheel(10, 10, 0, 50);
        input.Wheel(10, 10, 0, 50);
        Assert.Equal(["a", "a", "a"], targets);
        Assert.Equal(100, document.ScrollingElement!.ScrollTop);

        a.Remove();
        document.Body!.Append(a);
        input.Wheel(10, 10, 0, 0);
        Assert.Equal(["a", "a", "a", "b"], targets);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Wheel_LegacyMouseWheelEvent(bool passive)
    {
        var document = Load("<div id=scroller><div id=content></div></div>");
        var scroller = document.GetElementById("scroller")!;
        Event? received = null;
        string? type = null;
        scroller.AddEventListener("mousewheel", e => (received, type) = (e, e.Type), new AddEventListenerOptions { Passive = passive });
        document.BrowsingContext!.Input.Wheel(50, 50, 0, 40);

        // The listener gets the wheel event, whose type is mousewheel while it is called
        var wheel = Assert.IsType<WheelEvent>(received);
        Assert.Equal("mousewheel", type);
        Assert.Equal("wheel", wheel.Type);
        Assert.Equal(!passive, wheel.Cancelable);

        // A target that has a wheel listener does not get the legacy event
        received = null;
        scroller.AddEventListener("wheel", _ => { });
        document.BrowsingContext!.Input.Wheel(50, 50, 0, 40);
        Assert.Null(received);
    }

    [Fact]
    public void ScrollPosition_KeptWhenTheElementStopsBeingAScrollContainer()
    {
        var document = Load("<div id=scroller><div id=content></div></div>");
        var scroller = document.GetElementById("scroller")!;
        scroller.ScrollTop = 120;
        scroller.SetAttribute("style", "overflow: visible");
        Assert.Equal(0, scroller.ScrollTop);
        scroller.RemoveAttribute("style");
        Assert.Equal(120, scroller.ScrollTop);

        // The scroll position of an element that generates no box is reset
        scroller.SetAttribute("style", "display: none");
        Assert.Equal(0, scroller.ScrollTop);
        _ = document.Body!.ScrollHeight;
        scroller.RemoveAttribute("style");
        Assert.Equal(0, scroller.ScrollTop);
    }

    [Fact]
    public void Wheel_ScrollsTheScrollContainerOfAPseudoElement_AndKeepsItsPositionAfterALayout()
    {
        var document = Load("""
            <style>#target::before { display: block; content: "a\A b\A c\A d"; white-space: pre; line-height: 100px; height: 50px; width: 100px; overflow: scroll; }</style>
            <div id=dummy>dummy</div><div id=target></div>
            """);
        var target = document.GetElementById("target")!;
        var engine = document.GetLayoutEngine();
        var top = target.GetBoundingClientRect().Top;
        Assert.True(document.BrowsingContext!.Input.Wheel(10, top + 25, 0, 1000));
        RunFrames(3);
        var state = engine.TryGetScrollState(target, "before");
        Assert.NotNull(state);
        Assert.Equal(state.MaxOffset.Y, state.Offset.Y);

        ((HtmlElement)document.GetElementById("dummy")!).Style.SetProperty("display", "none");
        RunFrames(3);
        Assert.Equal(state.MaxOffset.Y, engine.TryGetScrollState(target, "before")!.Offset.Y);
    }

    [Fact]
    public void Pinch_ZoomsTheVisualViewportAroundThePosition()
    {
        var document = Load("<p>text</p>");
        var window = document.DefaultView!;
        var visualViewport = window.VisualViewport!;
        var events = Log(visualViewport, "resize", "scroll", "scrollend");
        var wheels = new List<(bool CtrlKey, double DeltaY)>();
        document.AddEventListener("wheel", e => wheels.Add((((WheelEvent)e).CtrlKey, ((WheelEvent)e).DeltaY)));
        var input = document.BrowsingContext!.Input;

        Assert.True(input.Pinch(400, 300, 2));
        Assert.Equal([(true, -100 * Math.Log(2))], wheels);
        Assert.Equal(2, visualViewport.Scale);
        Assert.Equal((400, 300), (visualViewport.Width, visualViewport.Height));
        Assert.Equal((200, 150), (visualViewport.OffsetLeft, visualViewport.OffsetTop));
        Assert.Equal((200, 150), (visualViewport.PageLeft, visualViewport.PageTop));

        // The layout viewport does not change
        Assert.Equal(800, window.InnerWidth);
        Assert.Equal(0, window.ScrollX);

        RunFrames();
        Assert.Equal(["resize", "scroll", "scrollend"], events);

        // The input stays in the coordinates of the layout viewport: the center of the zoomed screen is the center of the page,
        // and its top-left corner is the offset of the visual viewport
        Assert.Equal((400, 300), input.MapFromVisualViewport(400, 300));
        Assert.Equal((200, 150), input.MapFromVisualViewport(0, 0));

        // The zoom is limited to 5, and the page cannot be zoomed out further than its initial scale
        Assert.True(input.Pinch(400, 300, 10));
        Assert.Equal(5, visualViewport.Scale);
        Assert.True(input.Pinch(400, 300, 0.01));
        Assert.Equal(1, visualViewport.Scale);
        Assert.Equal((800, 600), (visualViewport.Width, visualViewport.Height));
        Assert.Equal((0, 0), (visualViewport.OffsetLeft, visualViewport.OffsetTop));
        Assert.False(input.Pinch(400, 300, 0.5));
    }

    [Fact]
    public void Pinch_CanceledWheelEventDoesNotZoom()
    {
        var document = Load("<p>text</p>");
        document.AddEventListener("wheel", e => e.PreventDefault(), new AddEventListenerOptions { Passive = false });
        Assert.False(document.BrowsingContext!.Input.Pinch(400, 300, 2));
        Assert.Equal(1, document.DefaultView!.VisualViewport!.Scale);
    }

    [Fact]
    public void Pinch_NotScalableMobilePage()
    {
        var context = _contexts.Create(new BrowsingContextOptions { TimeProvider = _clock, Viewport = new ViewportOptions { Width = 390, Height = 664, IsMobile = true } });
        var document = context.Navigate("https://example.com/", _ => "<!DOCTYPE html><meta name=viewport content='width=device-width, user-scalable=no'><p>text</p>");
        _contexts.EventLoop.RunUntilIdle();
        Assert.False(context.Input.Pinch(100, 100, 2));
        Assert.Equal(1, document.DefaultView!.VisualViewport!.Scale);

        var scalable = context.Navigate("https://example.com/", _ => "<!DOCTYPE html><meta name=viewport content='width=device-width, maximum-scale=3'><p>text</p>");
        _contexts.EventLoop.RunUntilIdle();
        Assert.True(context.Input.Pinch(100, 100, 4));
        Assert.Equal(3, scalable.DefaultView!.VisualViewport!.Scale);
    }

    [Fact]
    public void MapFromVisualViewport_IncludesTheScaleOfAMobilePage()
    {
        // The page without viewport meta element is laid out in 980px and scaled to the 98px wide device (scale 0.1)
        var context = _contexts.Create(new BrowsingContextOptions { TimeProvider = _clock, Viewport = new ViewportOptions { Width = 98, Height = 100, IsMobile = true } });
        var document = context.Navigate("https://example.com/", _ => "<!DOCTYPE html><p>text</p>");
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal(0.1, document.DefaultView!.VisualViewport!.Scale, 6);
        var (x, y) = context.Input.MapFromVisualViewport(5, 2);
        Assert.Equal(50, x, 6);
        Assert.Equal(20, y, 6);
    }

    [Fact]
    public void Wheel_ScrollsTheZoomedVisualViewportBeforeTheLayoutViewport()
    {
        var document = Load("<div style='height: 2000px'></div>");
        var window = document.DefaultView!;
        var visualViewport = window.VisualViewport!;
        var input = document.BrowsingContext!.Input;
        Assert.True(input.Pinch(0, 0, 2));
        Assert.Equal((0, 0), (visualViewport.OffsetLeft, visualViewport.OffsetTop));
        RunFrames();
        var events = Log(visualViewport, "scroll", "scrollend");

        // The visual viewport (300px high) scrolls to the bottom of the layout viewport (600px high), then the layout viewport scrolls
        Assert.True(input.Wheel(10, 10, 0, 500));
        RunFrames();
        Assert.Equal(300, visualViewport.OffsetTop);
        Assert.Equal(200, window.ScrollY);
        Assert.Equal(500, visualViewport.PageTop);
        Assert.Equal(["scroll", "scrollend"], events);
    }

    [Fact]
    public void ScrollIntoView_ScrollsTheZoomedVisualViewportBeforeTheLayoutViewport()
    {
        var document = Load("<div id=top style='height: 10px'></div><div style='height: 1000px'></div><div id=bottom style='height: 10px'></div><div style='height: 1000px'></div><div id=fixed style='position: fixed; left: 0; bottom: 0; width: 10px; height: 10px'></div>");
        var window = document.DefaultView!;
        var visualViewport = window.VisualViewport!;
        Assert.True(document.BrowsingContext!.Input.Pinch(400, 300, 2));
        Assert.Equal(150, visualViewport.OffsetTop);
        var events = Log(visualViewport, "scroll", "scrollend");

        // The visual viewport scrolls to the top of the layout viewport: the layout viewport does not scroll
        _ = document.GetElementById("top")!.ScrollIntoView();
        Assert.Equal(0, visualViewport.OffsetTop);
        Assert.Equal(0, window.ScrollY);

        // The visual viewport (300px high) scrolls to the bottom of the layout viewport, then the layout viewport scrolls the
        // rest: the target (1010px from the top of the document) is at the top of the visual viewport
        _ = document.GetElementById("bottom")!.ScrollIntoView();
        Assert.Equal(300, visualViewport.OffsetTop);
        Assert.Equal(710, window.ScrollY);
        Assert.Equal(1010, visualViewport.PageTop);

        // A fixed positioned target is in the layout viewport: only the visual viewport scrolls to it
        _ = document.GetElementById("top")!.ScrollIntoView();
        Assert.Equal(0, visualViewport.OffsetTop);
        Assert.Equal(0, window.ScrollY);
        _ = document.GetElementById("fixed")!.ScrollIntoView();
        Assert.Equal(300, visualViewport.OffsetTop);
        Assert.Equal(0, window.ScrollY);

        RunFrames();
        Assert.Equal(["scroll", "scrollend"], events);
    }

    [Fact]
    public void Keys_ScrollTheZoomedVisualViewport()
    {
        var document = Load("<div style='height: 2000px'></div>");
        var window = document.DefaultView!;
        var visualViewport = window.VisualViewport!;
        var input = document.BrowsingContext!.Input;
        Assert.True(input.Pinch(400, 300, 2));
        Assert.Equal(150, visualViewport.OffsetTop);

        // Home: the visual viewport goes to the top of the layout viewport, which is at the top of the document
        input.Keyboard.Press("Home");
        Assert.Equal(0, visualViewport.OffsetTop);
        Assert.Equal(0, window.ScrollY);

        // PageDown: a page of the visual viewport (87.5% of 300px)
        input.Keyboard.Press("PageDown");
        Assert.Equal(262.5, visualViewport.OffsetTop);
        Assert.Equal(0, window.ScrollY);

        // End: the visual viewport goes to the bottom of the layout viewport, which scrolls to the end of the document
        input.Keyboard.Press("End");
        Assert.Equal(300, visualViewport.OffsetTop);
        Assert.Equal(document.DocumentElement!.ScrollHeight - 600, window.ScrollY);
    }

    [Fact]
    public void ScrollIntoView_SmoothScrollOfTheZoomedVisualViewport()
    {
        var document = Load("<div id=top style='height: 10px'></div><div style='height: 2000px'></div>");
        var visualViewport = document.DefaultView!.VisualViewport!;
        Assert.True(document.BrowsingContext!.Input.Pinch(400, 300, 2));
        RunFrames();
        var events = Log(visualViewport, "scroll", "scrollend");

        var task = document.GetElementById("top")!.ScrollIntoView(new ScrollIntoViewOptions { Behavior = ScrollBehavior.Smooth });
        Assert.Equal(150, visualViewport.OffsetTop);
        Assert.False(task.IsCompleted);
        RunFrames(3);
        Assert.InRange(visualViewport.OffsetTop, 0.001, 149.999);
        Assert.False(task.IsCompleted);
        RunFrames(30);
        Assert.Equal(0, visualViewport.OffsetTop);
        Assert.True(task.IsCompletedSuccessfully);
        Assert.Equal("scrollend", events[^1]);
        Assert.Equal(1, events.Count(e => e == "scrollend"));
    }

    [Fact]
    public void ScrollIntoView_SmoothScrollOfTheVisualViewport_RepeatedAbortedAndScrollEnd()
    {
        var document = Load("<div id=top style='height: 10px'></div><div id=middle style='position: absolute; top: 150px; height: 10px'></div><div style='height: 2000px'></div>");
        var visualViewport = document.DefaultView!.VisualViewport!;
        var events = Log(visualViewport, "scrollend");
        var top = document.GetElementById("top")!;
        var smooth = new ScrollIntoViewOptions { Behavior = ScrollBehavior.Smooth };

        // The scrollend event of the pinch, queued before the smooth scroll starts, is fired at the end of the smooth scroll
        Assert.True(document.BrowsingContext!.Input.Pinch(400, 300, 2));
        var first = top.ScrollIntoView(smooth);
        RunFrames(3);
        Assert.Empty(events);

        // A smooth scroll to the same position continues the one in progress
        var offset = visualViewport.OffsetTop;
        var second = top.ScrollIntoView(smooth);
        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);
        RunFrames(30);
        Assert.Equal(0, visualViewport.OffsetTop);
        Assert.True(first.IsCompletedSuccessfully);
        Assert.True(second.IsCompletedSuccessfully);
        Assert.InRange(offset, 0.001, 149.999);
        Assert.Equal(["scrollend"], events);

        // A scroll that does not move the visual viewport aborts the smooth scroll in progress
        var middle = document.GetElementById("middle")!;
        _ = middle.ScrollIntoView();
        Assert.Equal(150, visualViewport.OffsetTop);
        var third = top.ScrollIntoView(smooth);
        RunFrames(3);
        var aborted = visualViewport.OffsetTop;
        Assert.InRange(aborted, 0.001, 149.999);
        // (the element is in the visual viewport: it is not moved by the nearest alignment)
        var instant = middle.ScrollIntoView(new ScrollIntoViewOptions { Block = ScrollLogicalPosition.Nearest });
        Assert.Equal(aborted, visualViewport.OffsetTop);
        Assert.True(third.IsCompleted);
        Assert.True(instant.IsCompleted);
        RunFrames(30);
        Assert.Equal(aborted, visualViewport.OffsetTop);
    }

    [Fact]
    public void ScrollIntoView_AbortedSmoothScrollKeepsTheScrollEndOfThePinch()
    {
        var document = Load("<div id=top style='height: 10px'></div><div id=visible style='position: absolute; left: 300px; top: 200px; width: 10px; height: 10px'></div><div style='height: 2000px'></div>");
        var visualViewport = document.DefaultView!.VisualViewport!;
        var events = Log(visualViewport, "scrollend");
        var top = document.GetElementById("top")!;

        // The pinch moves the visual viewport ((200, 150) to (600, 450)); its scrollend event is deferred by the smooth scroll, which a
        // scroll to an element already in the visual viewport aborts at once
        Assert.True(document.BrowsingContext!.Input.Pinch(400, 300, 2));
        _ = top.ScrollIntoView(new ScrollIntoViewOptions { Behavior = ScrollBehavior.Smooth });
        _ = document.GetElementById("visible")!.ScrollIntoView(new ScrollIntoViewOptions { Block = ScrollLogicalPosition.Nearest, Behavior = ScrollBehavior.Instant });
        Assert.Equal((200, 150), (visualViewport.OffsetLeft, visualViewport.OffsetTop));
        RunFrames(30);
        Assert.Equal((200, 150), (visualViewport.OffsetLeft, visualViewport.OffsetTop));
        Assert.Equal(["scrollend"], events);
    }

    [Fact]
    public void Touch_ScrollEndWhenTheOtherFingerIsReleasedLast()
    {
        var document = Load("<div style='height: 2000px'></div>");
        var visualViewport = document.DefaultView!.VisualViewport!;
        var input = document.BrowsingContext!.Input;
        Assert.True(input.Pinch(400, 300, 2));
        RunFrames();
        var events = Log(visualViewport, "scrollend");

        // A finger pans the visual viewport, a second finger touches the screen without moving, then the panning finger is
        // released first
        var finger1 = input.CreatePointer("touch");
        var finger2 = input.CreatePointer("touch");
        finger1.Move(400, 300);
        finger1.Down();
        finger1.Move(400, 250);
        Assert.Equal(200, visualViewport.OffsetTop);
        finger2.Move(100, 100);
        finger2.Down();
        finger1.Up();
        RunFrames();
        Assert.Empty(events);

        finger2.Up();
        RunFrames();
        Assert.Equal(["scrollend"], events);
    }

    [Fact]
    public void Touch_TwoFingersPinchZoom()
    {
        var document = Load("<p>text</p>");
        var visualViewport = document.DefaultView!.VisualViewport!;
        var pointerEvents = Log(document, "pointercancel");
        var viewportEvents = Log(visualViewport, "scroll", "scrollend");
        var input = document.BrowsingContext!.Input;
        var finger1 = input.CreatePointer("touch");
        var finger2 = input.CreatePointer("touch");
        finger1.Move(300, 300);
        finger1.Down();
        finger2.Move(500, 300);
        finger2.Down();

        // The distance between the fingers doubles: the point under the middle of the fingers (400, 300) moves under their new middle (500, 300)
        finger2.Move(700, 300);
        Assert.Equal(["pointercancel", "pointercancel"], pointerEvents);
        Assert.Equal(2, visualViewport.Scale);
        Assert.Equal((150, 150), (visualViewport.OffsetLeft, visualViewport.OffsetTop));
        RunFrames();
        Assert.Equal(["scroll"], viewportEvents);

        // The scrollend event is fired when the fingers are released
        finger2.Up();
        finger1.Up();
        RunFrames();
        Assert.Equal(["scroll", "scrollend"], viewportEvents);
    }

    [Fact]
    public void Touch_PinchZoomPreventedByTouchAction()
    {
        var document = Load("<div style='touch-action: pan-x pan-y; height: 500px'></div>");
        var input = document.BrowsingContext!.Input;
        var finger1 = input.CreatePointer("touch");
        var finger2 = input.CreatePointer("touch");
        finger1.Move(300, 300);
        finger1.Down();
        finger2.Move(500, 300);
        finger2.Down();
        finger2.Move(700, 300);
        finger2.Up();
        finger1.Up();
        Assert.Equal(1, document.DefaultView!.VisualViewport!.Scale);
    }
}
