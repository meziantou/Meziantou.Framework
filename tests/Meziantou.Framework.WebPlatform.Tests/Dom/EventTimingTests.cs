using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Html;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Dom;

/// <summary>
/// Event Timing (https://w3c.github.io/event-timing/): the trusted events of the user input are timed until the next rendering
/// update of the event loop, which queues their entries to the performance timeline.
/// </summary>
public sealed class EventTimingTests : IDisposable
{
    private const string Style = "<style>body { margin: 0 } #a, #b { width: 100px; height: 100px } #scroller { width: 100px; height: 100px; overflow: auto } #content { width: 100px; height: 400px }</style>";

    private readonly BrowsingContextScope _contexts = new();
    private readonly FakeTimeProvider _clock = new();

    public void Dispose() => _contexts.Dispose();

    private Document Load(string html, WebPlatformFeatures? features = null)
    {
        var context = _contexts.Create(new BrowsingContextOptions { TimeProvider = _clock, Features = features });
        var document = context.Navigate("https://example.com/", _ => "<!DOCTYPE html>" + Style + html);
        _contexts.EventLoop.RunUntilIdle();
        AlignToFrame(document);
        return document;
    }

    /// <summary>Runs the rendering update of the next frame, if one is scheduled: the virtual clock advances to the time of the frame.</summary>
    private void RunFrame()
    {
        if (_contexts.EventLoop.PromoteDueTimers() is { } delay)
        {
            _clock.Advance(delay);
        }

        _contexts.EventLoop.RunUntilIdle();
    }

    /// <summary>
    /// Sets the virtual clock 10 ms after a frame: the frame requested by the next events is due 6.67 ms later, so it runs as
    /// soon as the event loop runs after the listeners that last longer (<see cref="Block"/>).
    /// </summary>
    private void AlignToFrame(Document document)
    {
        document.DefaultView!.RequestAnimationFrame(_ => { });
        RunFrame();
        _clock.Advance(TimeSpan.FromMilliseconds(10));
    }

    private static List<PerformanceEventTiming> Observe(Document document, string type = "event", double? durationThreshold = null, bool buffered = false)
    {
        var entries = new List<PerformanceEventTiming>();
        var observer = new PerformanceObserver(document.DefaultView!, (list, _, _) => entries.AddRange(list.GetEntries().Cast<PerformanceEventTiming>()));
        observer.Observe(new PerformanceObserverInit { Type = type, Buffered = buffered ? true : null, DurationThreshold = durationThreshold });
        return entries;
    }

    /// <summary>Makes the listeners of an event type last a duration: the virtual clock advances while the event is dispatched.</summary>
    private void Block(EventTarget target, string type, int milliseconds) => target.AddEventListener(type, _ => _clock.Advance(TimeSpan.FromMilliseconds(milliseconds)));

    private static void Click(Document document, double x = 50, double y = 50)
    {
        var mouse = document.BrowsingContext!.Input.Mouse;
        mouse.Move(x, y);
        mouse.Down();
        mouse.Up();
    }

    private static void Key(Element target, string type, string key, uint keyCode)
        => EventDispatcher.Dispatch(new KeyboardEvent(type, new KeyboardEventInit { Key = key, KeyCode = keyCode, Bubbles = true, Cancelable = true, Composed = true }) { IsTrusted = true }, target);

    private static PerformanceEventTiming Single(List<PerformanceEventTiming> entries, string name) => Assert.Single(entries, entry => entry.Name == name);

    [Fact]
    public void Duration_IsTheTimeUntilTheNextRenderingUpdate_RoundedTo8Milliseconds()
    {
        var document = Load("<div id=a></div>");
        var entries = Observe(document, durationThreshold: 16);
        Block(document, "pointerdown", 120);
        var start = document.DefaultView!.Performance.Now();
        Click(document);

        Assert.Empty(entries);
        RunFrame();

        // The frame runs once the slow listener returned: 120 ms after the pointerdown event, and at the time of the events that follow it
        var pointerDown = Single(entries, "pointerdown");
        Assert.Equal("event", pointerDown.EntryType);
        Assert.Equal(start, pointerDown.StartTime);
        Assert.Equal(start, pointerDown.ProcessingStart);
        Assert.Equal(start + 120, pointerDown.ProcessingEnd);
        Assert.Equal(120, pointerDown.Duration);
        Assert.True(pointerDown.Cancelable);
        Assert.Same(document.GetElementById("a"), pointerDown.Target);
        Assert.Equal("DIV#a", pointerDown.TargetSelector);
        Assert.All(entries.FindAll(entry => entry.Name == "pointerenter"), entry => Assert.False(entry.Cancelable));

        // The events dispatched after the slow listener are not queued (less than 16 ms)
        Assert.DoesNotContain(entries, entry => entry.Name is "pointerup" or "click" or "mousedown");
    }

    [Fact]
    public void DurationThreshold_DefaultsTo104_AndIsAtLeast16()
    {
        var document = Load("<div id=a></div>");
        var defaultEntries = Observe(document);
        var lowEntries = Observe(document, durationThreshold: 0);
        var highEntries = Observe(document, durationThreshold: 200);
        Block(document, "pointerdown", 110);
        Block(document, "pointerup", 20);
        Click(document);
        RunFrame();

        // pointerdown lasts 130 ms (128), pointerup 20 ms (24), click 0 ms
        Assert.Equal(128, Single(defaultEntries, "pointerdown").Duration);
        Assert.DoesNotContain(defaultEntries, entry => entry.Name == "pointerup");
        Assert.Equal(24, Single(lowEntries, "pointerup").Duration);
        Assert.DoesNotContain(lowEntries, entry => entry.Name == "click");
        Assert.Empty(highEntries);
    }

    [Fact]
    public void Buffer_HoldsTheEntriesOf104MillisecondsAndTheFirstInput()
    {
        var document = Load("<div id=a></div>");
        Block(document, "pointerdown", 110);
        Block(document, "pointerup", 40);
        Click(document);
        RunFrame();

        // pointerdown lasts 150 ms (152), pointerup 40 ms
        var performance = document.DefaultView!.Performance;
        var firstInput = Assert.IsType<PerformanceEventTiming>(Assert.Single(performance.GetEntriesByType("first-input")));
        Assert.Equal("pointerdown", firstInput.Name);
        Assert.Equal("first-input", firstInput.EntryType);
        Assert.Equal(152, firstInput.Duration);
        Assert.NotEqual(0UL, firstInput.InteractionId);
        Assert.Contains(firstInput, performance.GetEntries());

        // The event timing entries are not available from the timeline
        Assert.Empty(performance.GetEntriesByType("event"));
        Assert.Empty(performance.GetEntriesByName("pointerdown", "event"));

        // A buffered observer sees the buffered entries (at least 104 ms), whatever its duration threshold
        var buffered = Observe(document, durationThreshold: 16, buffered: true);
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal(152, Single(buffered, "pointerdown").Duration);
        Assert.DoesNotContain(buffered, entry => entry.Name == "pointerup");
        var firstInputs = Observe(document, "first-input", buffered: true);
        _contexts.EventLoop.RunUntilIdle();
        Assert.Same(firstInput, Assert.Single(firstInputs));

        // The first input is reported once
        firstInputs.Clear();
        Click(document);
        RunFrame();
        Assert.Empty(firstInputs);
        Assert.Single(performance.GetEntriesByType("first-input"));
    }

    [Fact]
    public void InteractionId_OfAClick()
    {
        var document = Load("<div id=a></div>");
        var entries = Observe(document, durationThreshold: 16);
        Block(document, "click", 20);
        var performance = document.DefaultView!.Performance;
        Assert.Equal(0UL, performance.InteractionCount);
        Click(document);
        RunFrame();

        var id = Single(entries, "pointerdown").InteractionId;
        Assert.NotEqual(0UL, id);
        Assert.Equal(id, Single(entries, "pointerup").InteractionId);
        Assert.Equal(id, Single(entries, "click").InteractionId);
        Assert.Equal(0UL, Single(entries, "mousedown").InteractionId);
        Assert.Equal(0UL, Single(entries, "mouseup").InteractionId);
        Assert.Equal(1UL, performance.InteractionCount);

        entries.Clear();
        AlignToFrame(document);
        Click(document);
        RunFrame();
        Assert.True(Single(entries, "pointerdown").InteractionId > id);
        Assert.Equal(2UL, performance.InteractionCount);
    }

    [Fact]
    public void InteractionId_OfAPressedPointer_IsKnownWhenItIsReleased()
    {
        var document = Load("<div id=a></div>");
        var entries = Observe(document, durationThreshold: 16);
        Block(document, "pointerdown", 20);
        var mouse = document.BrowsingContext!.Input.Mouse;
        mouse.Move(50, 50);
        mouse.Down();
        RunFrame();

        // The duration of the pointerdown is set by the rendering update, but its entry waits for the release
        Assert.DoesNotContain(entries, entry => entry.Name == "pointerdown");
        Assert.Equal(1UL, document.DefaultView!.Performance.EventCounts!.Get("pointerdown"));

        // The duration of the pointerdown (20 ms) does not include the time the button stays pressed
        _clock.Advance(TimeSpan.FromSeconds(1));
        AlignToFrame(document);
        mouse.Up();
        RunFrame();
        var pointerDown = Single(entries, "pointerdown");
        Assert.Equal(24, pointerDown.Duration);
        Assert.NotEqual(0UL, pointerDown.InteractionId);
        Assert.Equal(1UL, document.DefaultView.Performance.EventCounts.Get("pointerdown"));

        // The pointerdown is the first input, although its duration was set before its interaction was known
        Assert.Equal("pointerdown", Assert.Single(document.DefaultView.Performance.GetEntriesByType("first-input")).Name);
    }

    [Fact]
    public void InteractionId_OfACanceledPointer_IsZero()
    {
        var document = Load("<div id=scroller><div id=content></div></div>");
        var entries = Observe(document, durationThreshold: 16);
        Block(document, "pointerdown", 30);
        var finger = document.BrowsingContext!.Input.CreatePointer("touch");
        finger.Move(50, 80);
        finger.Down();
        finger.Move(50, 30);
        finger.Up();
        RunFrame();

        // The touch panned: it is not an interaction
        Assert.Equal(0UL, Single(entries, "pointerdown").InteractionId);
        Assert.Equal(0UL, document.DefaultView!.Performance.InteractionCount);
        Assert.Empty(document.DefaultView.Performance.GetEntriesByType("first-input"));
        Assert.Equal(1UL, document.DefaultView.Performance.EventCounts!.Get("pointercancel"));
    }

    [Fact]
    public void InteractionId_OfAKeyPress()
    {
        var document = Load("<button id=a>button</button>");
        var button = document.GetElementById("a")!;
        var entries = Observe(document, durationThreshold: 16);
        foreach (var type in (string[])["keydown", "keypress", "keyup", "click"])
        {
            Block(document, type, 20);
        }

        Key(button, "keydown", "Enter", 13);
        Key(button, "keypress", "Enter", 13);
        Key(button, "keyup", "Enter", 13);
        RunFrame();

        // The interaction starts at keydown; the click simulated by the key belongs to it
        var id = Single(entries, "keydown").InteractionId;
        Assert.NotEqual(0UL, id);
        Assert.Equal(id, Single(entries, "keypress").InteractionId);
        Assert.Equal(id, Single(entries, "keyup").InteractionId);
        Assert.Equal(id, Single(entries, "click").InteractionId);
        Assert.Equal(1UL, document.DefaultView!.Performance.InteractionCount);
        Assert.Equal("keydown", Assert.Single(document.DefaultView.Performance.GetEntriesByType("first-input")).Name);

        // The release of a key that was not pressed is not an interaction
        entries.Clear();
        AlignToFrame(document);
        Key(button, "keyup", "a", 65);
        RunFrame();
        Assert.Equal(0UL, Single(entries, "keyup").InteractionId);
    }

    [Fact]
    public void EventCounts_CountTheEventsOfEachConsideredType()
    {
        var document = Load("<div id=a></div>");
        var counts = document.DefaultView!.Performance.EventCounts!;
        Assert.Equal(36, counts.Count);
        Assert.All(counts, pair => Assert.Equal(0UL, pair.Value));
        Assert.True(counts.Has("click"));
        Assert.False(counts.Has("pointermove"));
        Assert.Null(counts.Get("pointermove"));
        Assert.Same(counts, document.DefaultView.Performance.EventCounts);

        Click(document);

        // The events are counted by the rendering update that follows them, whatever their duration
        Assert.Equal(0UL, counts.Get("click"));
        RunFrame();
        Assert.Equal(1UL, counts.Get("click"));
        Assert.Equal(1UL, counts.Get("pointerdown"));
        Assert.Equal(1UL, counts.Get("mouseup"));
        Assert.Equal(1UL, counts.Get("pointerover"));
        Assert.Equal(0UL, counts.Get("keydown"));

        // The untrusted events are not counted
        document.GetElementById("a")!.DispatchEvent(new MouseEvent("click", new MouseEventInit { Bubbles = true }));
        ((HtmlElement)document.GetElementById("a")!).Click();
        RunFrame();
        Assert.Equal(1UL, counts.Get("click"));
    }

    [Fact]
    public void Target_IsNullWhenTheNodeIsRemoved()
    {
        var document = Load("<div id=a></div>");
        var entries = Observe(document, durationThreshold: 16);
        Block(document, "pointerdown", 20);
        Click(document);
        RunFrame();

        var pointerDown = Single(entries, "pointerdown");
        Assert.Same(document.GetElementById("a"), pointerDown.Target);
        document.GetElementById("a")!.Remove();
        Assert.Null(pointerDown.Target);
        Assert.Equal("DIV#a", pointerDown.TargetSelector);
    }

    [Fact]
    public void Target_IsTheLastTargetOfTheEvent_AndNullInAShadowTree()
    {
        var document = Load("<div id=b></div>");
        var host = document.GetElementById("b")!;
        var inner = document.CreateElement("div");
        inner.SetAttribute("style", "height: 50px");
        var other = document.CreateElement("div");
        other.SetAttribute("style", "height: 50px");
        host.AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Open }).Append(inner, other);
        var entries = Observe(document, durationThreshold: 16);
        Block(document, "pointerdown", 20);
        Block(other, "mouseover", 20);

        // The target of an event that leaves the shadow tree is the host; a mouseover between two nodes of the shadow tree
        // does not leave it
        var mouse = document.BrowsingContext!.Input.Mouse;
        mouse.Move(50, 20);
        mouse.Down();
        mouse.Up();
        mouse.Move(50, 70);
        RunFrame();

        Assert.Same(host, Single(entries, "pointerdown").Target);
        var mouseOvers = entries.FindAll(entry => entry.Name == "mouseover");
        Assert.Equal(2, mouseOvers.Count);
        Assert.Same(host, mouseOvers[0].Target);
        Assert.Equal("DIV#b", mouseOvers[0].TargetSelector);
        Assert.Null(mouseOvers[1].Target);
        Assert.Equal("DIV", mouseOvers[1].TargetSelector);
    }

    [Fact]
    public void DisabledFeature_TimesNoEvent()
    {
        var document = Load("<div id=a></div>", new WebPlatformFeatures { EventTimingEnabled = false });
        var performance = document.DefaultView!.Performance;
        Assert.DoesNotContain("event", performance.SupportedEntryTypes);
        Assert.DoesNotContain("first-input", performance.SupportedEntryTypes);
        var entries = Observe(document, durationThreshold: 16);
        Block(document, "pointerdown", 120);
        Click(document);

        // The events schedule no rendering update
        Assert.Null(_contexts.EventLoop.PromoteDueTimers());
        RunFrame();
        Assert.Empty(entries);
        Assert.Empty(performance.GetEntriesByType("first-input"));
        Assert.Equal(0UL, performance.EventCounts!.Get("click"));
        Assert.Equal(0UL, performance.InteractionCount);
    }

    [Fact]
    public void SupportedEntryTypes_ContainEventAndFirstInput()
    {
        var document = Load("<div id=a></div>");
        var types = document.DefaultView!.Performance.SupportedEntryTypes.ToList();

        Assert.Equal(types.IndexOf("element") + 1, types.IndexOf("event"));
        Assert.Equal(types.IndexOf("event") + 1, types.IndexOf("first-input"));
    }
}
