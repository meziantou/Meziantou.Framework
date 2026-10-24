using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Tests.Html;

public sealed class UserInputTests : IDisposable
{
    private const string Style = "<style>body { margin: 0 } #a, #b { width: 100px; height: 100px } #scroller { width: 100px; height: 100px; overflow: auto } #content { width: 100px; height: 400px }</style>";

    private readonly BrowsingContextScope _contexts = new();

    public void Dispose() => _contexts.Dispose();

    private Document Load(string html)
    {
        var document = _contexts.Create().Navigate("https://example.com/", _ => "<!DOCTYPE html>" + Style + html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    private static List<string> Log(Document document, params string[] types)
    {
        var events = new List<string>();
        foreach (var type in types)
        {
            document.AddEventListener(type, e => events.Add($"{e.Type}@{(e.Target as Element)?.Id}"), new AddEventListenerOptions { Capture = true });
        }

        return events;
    }

    [Fact]
    public void Mouse_MoveFiresTheBoundaryEventsAndTheCompatibilityMouseEvents()
    {
        var document = Load("<div id=a></div><div id=b></div>");
        var mouse = document.BrowsingContext!.Input.Mouse;
        mouse.Move(50, 50);
        var events = Log(document, "pointerover", "pointerout", "pointermove", "mouseover", "mouseout", "mousemove");
        mouse.Move(50, 150);

        Assert.Equal(["pointerout@a", "pointerover@b", "mouseout@a", "mouseover@b", "pointermove@b", "mousemove@b"], events);
        Assert.True(document.GetElementById("b")!.Matches(":hover"));
    }

    [Fact]
    public void Mouse_ClickFiresPointerEventsWithTheAttributesOfTheMouse()
    {
        var document = Load("<div id=a></div>");
        var events = new List<string>();
        foreach (var type in (string[])["pointerdown", "mousedown", "pointerup", "mouseup", "click"])
        {
            document.AddEventListener(type, e =>
            {
                var mouseEvent = (MouseEvent)e;
                var pointer = e as PointerEvent;
                events.Add($"{e.Type} {mouseEvent.ClientX},{mouseEvent.ClientY} button={mouseEvent.Button} buttons={mouseEvent.Buttons} id={pointer?.PointerId} type={pointer?.PointerType} trusted={e.IsTrusted}");
            });
        }

        var mouse = document.BrowsingContext!.Input.Mouse;
        mouse.Move(10.5, 20);
        mouse.Down();
        mouse.Up();

        Assert.Equal(
            [
                "pointerdown 10.5,20 button=0 buttons=1 id=1 type=mouse trusted=True",
                "mousedown 10,20 button=0 buttons=1 id= type= trusted=True",
                "pointerup 10.5,20 button=0 buttons=0 id=1 type=mouse trusted=True",
                "mouseup 10,20 button=0 buttons=0 id= type= trusted=True",
                "click 10,20 button=0 buttons=0 id=1 type=mouse trusted=True",
            ],
            events);
    }

    [Fact]
    public void Mouse_CanceledPointerDownSuppressesTheCompatibilityMouseEvents()
    {
        var document = Load("<div id=a></div>");
        document.AddEventListener("pointerdown", e => e.PreventDefault());
        var events = Log(document, "mousedown", "mouseup", "click");
        var mouse = document.BrowsingContext!.Input.Mouse;
        mouse.Move(10, 10);
        mouse.Down();
        mouse.Up();

        Assert.Equal(["click@a"], events);
    }

    [Fact]
    public void PointerCapture_RetargetsTheEventsUntilTheRelease()
    {
        var document = Load("<div id=a></div><div id=b></div>");
        var a = document.GetElementById("a")!;
        a.AddEventListener("pointerdown", e => a.SetPointerCapture(((PointerEvent)e).PointerId));
        var events = Log(document, "gotpointercapture", "lostpointercapture", "pointermove", "pointerup");
        var mouse = document.BrowsingContext!.Input.Mouse;
        mouse.Move(50, 50);
        events.Clear();
        mouse.Down();
        Assert.True(a.HasPointerCapture(1));
        mouse.Move(50, 150);
        mouse.Up();

        Assert.Equal(["gotpointercapture@a", "pointermove@a", "pointerup@a", "lostpointercapture@a"], events);
        Assert.False(a.HasPointerCapture(1));
        Assert.Throws<DomException>(() => a.SetPointerCapture(42));
    }

    [Fact]
    public void Touch_TapFiresThePointerEventsThenTheCompatibilityMouseEvents()
    {
        var document = Load("<div id=a></div>");
        var events = Log(document, "pointerover", "pointerdown", "touchstart", "pointerup", "pointerout", "touchend", "mousemove", "mousedown", "mouseup", "click");
        var input = document.BrowsingContext!.Input;
        var finger = input.CreatePointer("touch");
        finger.Move(10, 10);
        Assert.Empty(events);
        finger.Down();
        Assert.NotEqual(input.Mouse.PointerId, finger.PointerId);
        Assert.True(finger.IsPrimary);
        finger.Up();

        Assert.Equal(["pointerover@a", "pointerdown@a", "touchstart@a", "pointerup@a", "pointerout@a", "touchend@a", "mousemove@a", "mousedown@a", "mouseup@a", "click@a"], events);
    }

    [Fact]
    public void Touch_TargetsOfTheTouchPointsAreRetargeted()
    {
        var document = Load("<div id=a></div>");
        var host = document.GetElementById("a")!;
        var shadowRoot = host.AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Closed });
        shadowRoot.InnerHtml = "<div id=inner style='height: 100px'></div>";
        var inner = shadowRoot.QuerySelector("#inner")!;

        var targets = new List<(EventTarget? Target, EventTarget TouchTarget, bool SameList)>();
        void Listener(Event e)
        {
            var touchEvent = (TouchEvent)e;
            targets.Add((e.Target, touchEvent.ChangedTouches[0].Target, ReferenceEquals(touchEvent.ChangedTouches, touchEvent.ChangedTouches) && ReferenceEquals(touchEvent.ChangedTouches[0], touchEvent.Touches[0])));
        }

        document.DefaultView!.AddEventListener("touchstart", Listener, new AddEventListenerOptions { Capture = true });
        inner.AddEventListener("touchstart", Listener);
        host.AddEventListener("touchstart", Listener);

        var finger = document.BrowsingContext!.Input.CreatePointer("touch");
        finger.Move(10, 10);
        finger.Down();
        finger.Up();

        // A listener outside of the shadow tree sees the shadow host as the target of the event and of its touch points
        Assert.Equal(new (EventTarget?, EventTarget, bool)[] { (host, host, true), (inner, inner, true), (host, host, true) }, targets);
    }

    [Theory]
    [InlineData("auto", true)]
    [InlineData("pan-y", true)]
    [InlineData("pan-x", false)]
    [InlineData("none", false)]
    public void Touch_PanScrollsAndCancelsThePointerWhenTouchActionAllowsIt(string touchAction, bool pans)
    {
        var document = Load($"<div id=scroller style='touch-action: {touchAction}'><div id=content></div></div>");
        var scroller = document.GetElementById("scroller")!;
        var events = Log(document, "pointercancel", "pointerup");
        var finger = document.BrowsingContext!.Input.CreatePointer("touch");
        finger.Move(50, 80);
        finger.Down();
        finger.Move(50, 60);
        finger.Move(50, 30);
        finger.Up();

        Assert.Equal([pans ? "pointercancel@content" : "pointerup@content"], events);
        Assert.Equal(pans ? 50 : 0, scroller.ScrollTop);
    }

    [Fact]
    public void Wheel_ScrollsTheScrollContainerUnderThePointerUnlessCanceled()
    {
        var document = Load("<div id=scroller><div id=content></div></div>");
        var scroller = document.GetElementById("scroller")!;
        var input = document.BrowsingContext!.Input;
        Assert.True(input.Wheel(50, 50, 0, 30));
        Assert.Equal(30, scroller.ScrollTop);

        // The scroll is chained to the viewport (which cannot scroll) when the container is at its limit
        Assert.False(input.Wheel(50, 50, 0, -100) && input.Wheel(50, 50, 0, -100));
        Assert.Equal(0, scroller.ScrollTop);

        scroller.AddEventListener("wheel", e => e.PreventDefault());
        Assert.False(input.Wheel(50, 50, 0, 30));
        Assert.Equal(0, scroller.ScrollTop);
    }

    [Fact]
    public void RemovedTarget_TheNextEventsAreFiredAtItsParent()
    {
        var document = Load("<div id=a><div id=b></div></div>");
        var b = document.GetElementById("b")!;
        b.AddEventListener("pointerdown", _ => b.Remove());
        var events = Log(document, "pointerover", "pointerdown", "pointerup", "mousedown", "click");
        var mouse = document.BrowsingContext!.Input.Mouse;
        mouse.Move(50, 50);
        mouse.Down();
        mouse.Up();

        Assert.Equal(["pointerover@b", "pointerdown@b", "mousedown@a", "pointerover@a", "pointerup@a", "click@a"], events);
    }

    [Fact]
    public void Slider_DragSetsTheValue()
    {
        var document = Load("<input id=range type=range min=0 max=10 value=5 style='width: 116px; margin: 0; padding: 0; border: 0'>");
        var range = (HtmlInputElement)document.GetElementById("range")!;
        var events = Log(document, "input", "change");
        var mouse = document.BrowsingContext!.Input.Mouse;
        mouse.Move(58, 5);
        mouse.Down();
        mouse.Move(200, 5);
        Assert.Equal("10", range.Value);
        mouse.Up();

        Assert.Equal(["input@range", "change@range"], events);
    }

    [Theory]
    [InlineData("date")]
    [InlineData("datetime-local")]
    [InlineData("month")]
    [InlineData("week")]
    [InlineData("time")]
    [InlineData("color")]
    public void InputPicker_ClickOpensItAndAClickOutsideClosesIt(string type)
    {
        var document = Load($"<input id=input type={type} style='display: block; width: 100px; height: 20px; margin: 0'><div id=a></div>");
        var input = (HtmlInputElement)document.GetElementById("input")!;
        var mouse = document.BrowsingContext!.Input.Mouse;
        Assert.False(input.Matches(":open"));

        mouse.Move(50, 10);
        mouse.Down();
        mouse.Up();
        Assert.True(input.Matches(":open"));
        Assert.False(document.DefaultView!.HasTransientActivation);

        // A press on the input element keeps the picker open
        mouse.Down();
        Assert.True(input.Matches(":open"));
        mouse.Up();

        mouse.Move(50, 70);
        mouse.Down();
        Assert.False(input.Matches(":open"));
        mouse.Up();
    }

    [Fact]
    public void InputPicker_ShowPickerOpensItUntilEscapeOrAnotherPicker()
    {
        var document = Load("<div id=a></div><input id=date type=date><input id=time type=time><input id=text list=list><datalist id=list><option>x</option></datalist><input id=plain>");
        var date = (HtmlInputElement)document.GetElementById("date")!;
        var time = (HtmlInputElement)document.GetElementById("time")!;
        var text = (HtmlInputElement)document.GetElementById("text")!;
        var plain = (HtmlInputElement)document.GetElementById("plain")!;
        var mouse = document.BrowsingContext!.Input.Mouse;
        void Activate()
        {
            mouse.Move(50, 50);
            mouse.Down();
            mouse.Up();
        }

        var error = Assert.Throws<DomException>(date.ShowPicker);
        Assert.Equal("NotAllowedError", error.Name);
        Assert.False(date.Matches(":open"));

        Activate();
        date.ShowPicker();
        Assert.True(date.Matches(":open"));
        Assert.False(document.DefaultView!.HasTransientActivation);
        Assert.False(date.Matches(":focus"));

        Activate();
        Assert.False(date.Matches(":open"));
        time.ShowPicker();
        Assert.True(time.Matches(":open"));

        EventDispatcher.Dispatch(new KeyboardEvent("keydown", new KeyboardEventInit { Key = "Escape", Bubbles = true, Cancelable = true, Composed = true }) { IsTrusted = true }, document.Body!);
        Assert.False(time.Matches(":open"));

        // The suggestions of the datalist are the picker of a text input; an input without picker only consumes the activation
        Activate();
        text.ShowPicker();
        Assert.True(text.Matches(":open"));
        text.RemoveAttribute("list");
        Assert.False(text.Matches(":open"));

        Activate();
        plain.ShowPicker();
        Assert.False(plain.Matches(":open"));
        Assert.False(document.DefaultView.HasTransientActivation);

        // The picker closes when the element changes its type, is no longer mutable or is removed
        Activate();
        date.ShowPicker();
        date.Type = "text";
        Assert.False(date.Matches(":open"));

        Activate();
        time.ShowPicker();
        time.SetAttribute("readonly", "");
        Assert.False(time.Matches(":open"));
        time.RemoveAttribute("readonly");

        Activate();
        time.ShowPicker();
        time.Remove();
        Assert.False(time.Matches(":open"));
    }

    [Fact]
    public void Keyboard_Modifiers()
    {
        var document = Load("<div id=a></div>");
        var input = document.BrowsingContext!.Input;
        input.Modifiers = InputModifiers.Shift | InputModifiers.Meta;
        MouseEvent? received = null;
        document.AddEventListener("pointermove", e => received = (MouseEvent)e);
        input.Mouse.Move(10, 10);

        Assert.NotNull(received);
        Assert.True(received.ShiftKey);
        Assert.True(received.MetaKey);
        Assert.False(received.CtrlKey);
    }
}
