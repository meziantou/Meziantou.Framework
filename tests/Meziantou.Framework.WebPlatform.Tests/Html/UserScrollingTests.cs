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
}
