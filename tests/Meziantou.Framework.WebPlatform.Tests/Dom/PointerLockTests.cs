using Meziantou.Framework.WebPlatform.Dom;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Dom;

/// <summary>https://w3c.github.io/pointerlock/</summary>
public sealed class PointerLockTests : IDisposable
{
    private const string Page = "<!DOCTYPE html><style>body { margin: 0 } div { width: 100px; height: 100px }</style><div id=a></div><div id=b></div>";

    private readonly BrowsingContextScope _contexts = new();
    private readonly FakeTimeProvider _clock = new();

    public void Dispose() => _contexts.Dispose();

    private Document Load(string html, BrowsingContextOptions? options = null)
    {
        var document = _contexts.Create(options ?? new BrowsingContextOptions { TimeProvider = _clock }).Navigate("https://example.com/", _ => html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    private static T Get<T>(Document document, string id)
        where T : Element => (T)document.GetElementById(id)!;

    /// <summary>Gives the window transient activation, as an activation triggering input event of the user does.</summary>
    private static void Activate(Document document) => Window.NotifyActivation(document);

    private static void SendKey(Document document, string type, string key, bool cancel = false)
    {
        var target = document.ActiveElement ?? document.DocumentElement!;
        if (cancel)
        {
            target.AddEventListener(type, e => e.PreventDefault());
        }

        var @event = new KeyboardEvent(type, new KeyboardEventInit { Key = key, Bubbles = true, Cancelable = true }) { IsTrusted = true };
        EventDispatcher.Dispatch(@event, target);
    }

    private Element Lock(Element element, PointerLockOptions? options = null)
    {
        Activate(element.NodeDocument);
        var task = element.RequestPointerLock(options);
        _contexts.EventLoop.RunUntilIdle();
        Assert.True(task.IsCompletedSuccessfully);
        return element;
    }

    private static List<string> RecordLockEvents(Document document)
    {
        var events = new List<string>();
        foreach (var type in new[] { "pointerlockchange", "pointerlockerror" })
        {
            document.AddEventListener(type, e => events.Add($"{e.Type} {(e.Target is Document ? "document" : "?")} {e.Bubbles} {e.Cancelable}"));
        }

        return events;
    }

    private static List<string> RecordMouseEvents(params Element[] elements)
    {
        var events = new List<string>();
        foreach (var element in elements)
        {
            foreach (var type in new[] { "pointermove", "mousemove", "pointerdown", "mousedown", "pointerup", "mouseup", "click", "mouseover", "mouseout", "mouseenter", "mouseleave", "pointerover", "pointerout", "pointerenter", "pointerleave" })
            {
                element.AddEventListener(type, e =>
                {
                    if (ReferenceEquals(e.Target, element))
                    {
                        var mouseEvent = (MouseEvent)e;
                        events.Add($"{e.Type} {element.Id} {mouseEvent.ClientX},{mouseEvent.ClientY} {mouseEvent.MovementX},{mouseEvent.MovementY}");
                    }
                });
            }
        }

        return events;
    }

    private static string GetErrorName(Task task) => Assert.IsType<DomException>(task.Exception!.InnerException).Name;

    [Fact]
    public void RequestPointerLock_WithoutUserActivation_Fails()
    {
        var document = Load(Page);
        var events = RecordLockEvents(document);

        var task = Get<Element>(document, "a").RequestPointerLock();
        Assert.False(task.IsCompleted);
        _contexts.EventLoop.RunUntilIdle();

        Assert.Equal("NotAllowedError", GetErrorName(task));
        Assert.Null(document.PointerLockElement);
        Assert.Equal(["pointerlockerror document False False"], events);
    }

    [Fact]
    public void RequestPointerLock_LocksThePointerFromATask()
    {
        var document = Load(Page);
        var events = RecordLockEvents(document);
        var element = Get<Element>(document, "a");

        Activate(document);
        var task = element.RequestPointerLock();

        // The lock is pending: the pointer is locked by a task
        Assert.Null(document.PointerLockElement);
        _contexts.EventLoop.RunUntilIdle();
        Assert.True(task.IsCompletedSuccessfully);
        Assert.Same(element, document.PointerLockElement);
        Assert.Equal(["pointerlockchange document False False"], events);

        // The request does not consume the user activation
        Assert.True(document.DefaultView!.HasTransientActivation);

        // The pointer-lock target changes without asking the host again
        var other = Get<Element>(document, "b");
        task = other.RequestPointerLock();
        _contexts.EventLoop.RunUntilIdle();
        Assert.True(task.IsCompletedSuccessfully);
        Assert.Same(other, document.PointerLockElement);

        document.ExitPointerLock();
        Assert.Null(document.PointerLockElement);
        Assert.Equal(2, events.Count);
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal(3, events.Count);

        // exitPointerLock() does nothing when the pointer is not locked
        document.ExitPointerLock();
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal(3, events.Count);
    }

    [Fact]
    public void RequestPointerLock_ElementThatIsNotConnected_Fails()
    {
        var document = Load(Page);
        var events = RecordLockEvents(document);

        Activate(document);
        var task = document.CreateElement("div").RequestPointerLock();
        _contexts.EventLoop.RunUntilIdle();

        Assert.Equal("WrongDocumentError", GetErrorName(task));
        Assert.Equal(["pointerlockerror document False False"], events);

        // The element is removed while the request is pending
        var element = Get<Element>(document, "a");
        task = element.RequestPointerLock();
        element.Remove();
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal("WrongDocumentError", GetErrorName(task));
        Assert.Null(document.PointerLockElement);
    }

    [Fact]
    public void RequestPointerLock_WithoutSystemFocus_Fails()
    {
        var document = Load(Page);
        document.BrowsingContext!.HasSystemFocus = false;

        Activate(document);
        var task = Get<Element>(document, "a").RequestPointerLock();
        _contexts.EventLoop.RunUntilIdle();

        Assert.Equal("WrongDocumentError", GetErrorName(task));
    }

    [Fact]
    public void RequestPointerLock_SandboxedDocument_NeedsAllowPointerLock()
    {
        var document = Load("<iframe id=sandboxed sandbox='allow-scripts' srcdoc='<div id=a></div>'></iframe><iframe id=allowed sandbox='allow-pointer-lock' srcdoc='<div id=a></div>'></iframe>");
        var sandboxed = document.BrowsingContext!.Children[0].Document;
        var allowed = document.BrowsingContext.Children[1].Document;

        Activate(sandboxed);
        var task = Get<Element>(sandboxed, "a").RequestPointerLock();
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal("SecurityError", GetErrorName(task));

        Lock(Get<Element>(allowed, "a"));
        Assert.Same(Get<Element>(allowed, "a"), allowed.PointerLockElement);

        // The pointer-lock element belongs to the document of the element
        Assert.Null(document.PointerLockElement);
        Assert.Null(sandboxed.PointerLockElement);
    }

    [Fact]
    public void RequestPointerLock_WhileAnotherDocumentHasTheLock_Fails()
    {
        var document = Load("<div id=a></div><iframe id=frame srcdoc='<div id=a></div>'></iframe>");
        var inner = Get<HtmlIFrameElement>(document, "frame").ContentDocument!;
        Lock(Get<Element>(document, "a"));

        Activate(inner);
        var task = Get<Element>(inner, "a").RequestPointerLock();
        _contexts.EventLoop.RunUntilIdle();

        Assert.Equal("InvalidStateError", GetErrorName(task));
        Assert.Same(Get<Element>(document, "a"), document.PointerLockElement);

        // exitPointerLock() of another document does not exit the lock
        inner.ExitPointerLock();
        Assert.NotNull(document.PointerLockElement);
    }

    [Fact]
    public void ExitPointerLock_AllowsTheNextRequestWithoutUserActivation()
    {
        var document = Load(Page);
        var element = Lock(Get<Element>(document, "a"));
        document.ExitPointerLock();
        _clock.Advance(TimeSpan.FromSeconds(30));
        _contexts.EventLoop.RunUntilIdle();
        Assert.False(document.DefaultView!.HasTransientActivation);

        var task = element.RequestPointerLock();
        _contexts.EventLoop.RunUntilIdle();
        Assert.True(task.IsCompletedSuccessfully);
        Assert.Same(element, document.PointerLockElement);

        // The default unlock gesture of the user: the next request needs a user activation
        SendKey(document, "keydown", "Escape", cancel: true);
        Assert.Null(document.PointerLockElement);
        task = element.RequestPointerLock();
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal("NotAllowedError", GetErrorName(task));
    }

    [Fact]
    public void PointerLock_IsExitedByTheRemovalOfTheTargetAndByTheLossOfTheFocus()
    {
        var document = Load(Page);
        var events = RecordLockEvents(document);
        var element = Lock(Get<Element>(document, "a"));

        element.Remove();
        Assert.Null(document.PointerLockElement);
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal(2, events.Count);

        document.Body!.AppendChild(element);
        Lock(element);
        document.BrowsingContext!.HasSystemFocus = false;
        Assert.Null(document.PointerLockElement);
        document.BrowsingContext.HasSystemFocus = true;

        Lock(element);
        document.BrowsingContext.IsVisible = false;
        Assert.Null(document.PointerLockElement);
        document.BrowsingContext.IsVisible = true;

        Lock(element);
        document.BrowsingContext.ExitPointerLock();
        Assert.Null(document.PointerLockElement);
    }

    [Fact]
    public void PointerLock_IsExitedWhenTheDocumentOfTheTargetIsUnloaded()
    {
        var exits = 0;
        var document = Load("<iframe id=frame srcdoc='<div id=a></div>'></iframe>", new BrowsingContextOptions { TimeProvider = _clock, ExitPointerLock = _ => exits++ });
        var iframe = Get<HtmlIFrameElement>(document, "frame");
        Lock(Get<Element>(iframe.ContentDocument!, "a"));
        Assert.NotNull(document.BrowsingContext!.PointerLockTarget);

        iframe.Remove();
        Assert.Null(document.BrowsingContext.PointerLockTarget);
        Assert.Equal(1, exits);
    }

    [Fact]
    public void PointerLockElement_IsRetargetedOutOfTheShadowTrees()
    {
        var document = Load("<div id=host></div><div id=other></div>");
        var host = Get<Element>(document, "host");
        var root = host.AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Open });
        var inner = document.CreateElement("canvas");
        root.AppendChild(inner);
        var otherRoot = Get<Element>(document, "other").AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Open });

        Lock(inner);

        Assert.Same(host, document.PointerLockElement);
        Assert.Same(inner, root.PointerLockElement);
        Assert.Null(otherRoot.PointerLockElement);
    }

    [Fact]
    public void Host_CanRefuseTheLockAndIsToldWhenItEnds()
    {
        var requests = new List<bool>();
        var exits = 0;
        var accept = false;
        var document = Load(Page, new BrowsingContextOptions
        {
            TimeProvider = _clock,
            RequestPointerLock = (_, options) =>
            {
                requests.Add(options.UnadjustedMovement);
                return accept;
            },
            ExitPointerLock = _ => exits++,
        });
        var events = RecordLockEvents(document);
        var element = Get<Element>(document, "a");

        Activate(document);
        var task = element.RequestPointerLock();
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal("NotSupportedError", GetErrorName(task));
        Assert.Equal(["pointerlockerror document False False"], events);
        Assert.Equal(0, exits);

        accept = true;
        Lock(element);

        // The same options: the host is not asked again; other options: it is
        Lock(Get<Element>(document, "b"));
        Assert.Equal([false, false], requests);
        Lock(element, new PointerLockOptions { UnadjustedMovement = true });
        Assert.Equal([false, false, true], requests);

        document.ExitPointerLock();
        Assert.Equal(1, exits);
    }

    [Fact]
    public void LockedMouse_FiresItsEventsAtTheTargetWithTheMovements()
    {
        var document = Load(Page);
        var a = Get<Element>(document, "a");
        var b = Get<Element>(document, "b");
        var input = document.BrowsingContext!.Input;
        input.Mouse.Move(10, 20);
        Lock(a);
        var events = RecordMouseEvents(a, b);

        // The pointer keeps its position: the moves are movements, and there is no boundary event
        input.Mouse.Move(15, 150);
        input.Mouse.MoveBy(-3, 4);
        Assert.Equal((10, 20), (input.Mouse.X, input.Mouse.Y));
        input.Mouse.Down();
        input.Mouse.Up();

        Assert.Equal(
            [
                "pointermove a 10,20 5,130", "mousemove a 10,20 5,130",
                "pointermove a 10,20 -3,4", "mousemove a 10,20 -3,4",
                "pointerdown a 10,20 0,0", "mousedown a 10,20 0,0",
                "pointerup a 10,20 0,0", "mouseup a 10,20 0,0",
                "click a 10,20 0,0",
            ],
            events);

        // The cursor is displayed again at its position: the pointer moves from there
        document.ExitPointerLock();
        events.Clear();
        input.Mouse.Move(10, 150);
        Assert.Contains("pointermove b 10,150 0,130", events);
        Assert.Contains("mouseover b 10,150 0,0", events);
    }

    [Fact]
    public void LockedMouse_WheelScrollsFromTheTarget()
    {
        var document = Load("<!DOCTYPE html><style>body { margin: 0 } div { width: 100px; height: 100px; overflow: scroll }</style><div id=a><p style='height: 1000px'></p></div><div id=b><p style='height: 1000px'></p></div>");
        var a = Get<Element>(document, "a");
        var b = Get<Element>(document, "b");
        var input = document.BrowsingContext!.Input;
        input.Mouse.Move(10, 10);
        Lock(a);
        Element? target = null;
        document.AddEventListener("wheel", e => target = e.Target as Element);

        Assert.True(input.Wheel(10, 150, 0, 30));

        Assert.Same(a, target);
        Assert.Equal(30, a.ScrollTop);
        Assert.Equal(0, b.ScrollTop);
    }

    [Fact]
    public void LockedMouse_ReleasesAndRefusesThePointerCapture()
    {
        var document = Load(Page);
        var a = Get<Element>(document, "a");
        var b = Get<Element>(document, "b");
        var input = document.BrowsingContext!.Input;
        var events = new List<string>();
        b.AddEventListener("gotpointercapture", e => events.Add(e.Type));
        b.AddEventListener("lostpointercapture", e => events.Add(e.Type));
        input.Mouse.Move(10, 10);
        input.Mouse.Down();
        b.SetPointerCapture(1);
        input.Mouse.Move(11, 11);
        Assert.Equal(["gotpointercapture"], events);

        Lock(a);
        input.Mouse.Move(12, 12);

        Assert.Equal(["gotpointercapture", "lostpointercapture"], events);
        Assert.Equal("InvalidStateError", Assert.Throws<DomException>(() => b.SetPointerCapture(1)).Name);
    }
}
