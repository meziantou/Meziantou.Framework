using Meziantou.Framework.WebPlatform.Dom;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Dom;

/// <summary>https://fullscreen.spec.whatwg.org/</summary>
public sealed class FullscreenTests : IDisposable
{
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

    /// <summary>Dispatches a trusted keyboard event, as the host does for the user input.</summary>
    private static void SendKey(Document document, string type, string key)
    {
        var @event = new KeyboardEvent(type, new KeyboardEventInit { Key = key, Bubbles = true, Cancelable = true }) { IsTrusted = true };
        EventDispatcher.Dispatch(@event, document.ActiveElement ?? document.DocumentElement!);
    }

    private Element RequestFullscreen(Element element, FullscreenOptions? options = null)
    {
        Activate(element.NodeDocument);
        var task = element.RequestFullscreen(options);
        _contexts.EventLoop.RunUntilIdle();
        Assert.True(task.IsCompletedSuccessfully);
        return element;
    }

    private static List<string> RecordEvents(Document document)
    {
        var events = new List<string>();
        foreach (var type in new[] { "fullscreenchange", "fullscreenerror" })
        {
            document.AddEventListener(type, e => events.Add($"{e.Type} {(e.Target as Element)?.Id ?? "document"}"));
        }

        return events;
    }

    [Fact]
    public void RequestFullscreen_WithoutUserActivation_Fails()
    {
        var document = Load("<div id=a></div>");
        var events = RecordEvents(document);

        var task = Get<Element>(document, "a").RequestFullscreen();
        Assert.False(task.IsCompleted);
        _contexts.EventLoop.RunUntilIdle();

        Assert.IsType<TypeErrorException>(task.Exception!.InnerException);
        Assert.Null(document.FullscreenElement);
        Assert.Equal(["fullscreenerror a"], events);
    }

    [Fact]
    public void RequestFullscreen_DisplaysTheElementFullscreen()
    {
        var document = Load("<!DOCTYPE html><style>#a { width: 10px; margin: 5px; transform: scale(2) }</style><div id=a>text</div>");
        var events = RecordEvents(document);
        var element = Get<HtmlElement>(document, "a");
        Assert.True(document.FullscreenEnabled);
        Assert.False(element.Matches(":fullscreen"));

        Activate(document);
        var task = element.RequestFullscreen();

        // The element is displayed fullscreen by a task, and the event is fired by the rendering update that follows
        Assert.Null(document.FullscreenElement);
        Assert.False(document.DefaultView!.HasTransientActivation);
        _contexts.EventLoop.RunUntilIdle();
        Assert.True(task.IsCompletedSuccessfully);
        Assert.Same(element, document.FullscreenElement);
        Assert.True(document.Fullscreen);
        Assert.Equal(["fullscreenchange a"], events);
        Assert.True(element.Matches(":fullscreen"));
        Assert.True(element.Matches(":modal"));
        Assert.True(document.TopLayerContains(element));

        // The rules of the user agent style sheet: the element fills the viewport
        var style = document.DefaultView.GetComputedStyle(element);
        Assert.Equal("fixed", style.GetPropertyValue("position"));
        Assert.Equal("none", style.GetPropertyValue("transform"));
        var rect = element.GetBoundingClientRect();
        Assert.Equal((0, 0, 800, 600), (rect.X, rect.Y, rect.Width, rect.Height));
        Assert.Equal("rgb(0, 0, 0)", document.DefaultView.GetComputedStyle(element, "::backdrop").GetPropertyValue("background-color"));

        var exit = document.ExitFullscreen();
        Assert.Same(element, document.FullscreenElement);
        _contexts.EventLoop.RunUntilIdle();
        Assert.True(exit.IsCompletedSuccessfully);
        Assert.Null(document.FullscreenElement);
        Assert.False(element.Matches(":fullscreen"));
        Assert.False(document.TopLayerContains(element));
        Assert.Equal(["fullscreenchange a", "fullscreenchange a"], events);
    }

    [Fact]
    public void ExitFullscreen_WithoutFullscreenElement_Fails()
    {
        var document = Load("<div id=a></div>");

        var task = document.ExitFullscreen();
        Assert.IsType<TypeErrorException>(task.Exception!.InnerException);
    }

    [Fact]
    public void ExitFullscreen_RestoresThePreviousFullscreenElement()
    {
        var document = Load("<div id=a><div id=b></div></div>");
        var events = RecordEvents(document);
        var outer = RequestFullscreen(Get<Element>(document, "a"));
        var inner = RequestFullscreen(Get<Element>(document, "b"));
        Assert.Same(inner, document.FullscreenElement);
        Assert.True(outer.Matches(":fullscreen"));

        _ = document.ExitFullscreen();
        _contexts.EventLoop.RunUntilIdle();
        Assert.Same(outer, document.FullscreenElement);
        Assert.False(inner.Matches(":fullscreen"));

        _ = document.ExitFullscreen();
        _contexts.EventLoop.RunUntilIdle();
        Assert.Null(document.FullscreenElement);
        Assert.Equal(["fullscreenchange a", "fullscreenchange b", "fullscreenchange b", "fullscreenchange a"], events);
    }

    [Theory]
    [InlineData("<dialog id=a></dialog>")]
    [InlineData("<div id=a popover></div><script>document.getElementById('a').showPopover()</script>")]
    [InlineData("<svg><rect id=a /></svg>")]
    [InlineData("<math><mi id=a></mi></math>")]
    public void RequestFullscreen_ElementsThatCannotBeFullscreen(string html)
    {
        var document = Load(html);
        if (document.GetElementById("a") is HtmlElement { PopoverAttributeState: not PopoverState.None } popover)
        {
            popover.ShowPopover();
        }

        Activate(document);
        var task = document.GetElementById("a")!.RequestFullscreen();
        _contexts.EventLoop.RunUntilIdle();

        Assert.True(task.IsFaulted);
        Assert.Null(document.FullscreenElement);

        // The user activation is not consumed by a request that fails
        Assert.True(document.DefaultView!.HasTransientActivation);
    }

    [Fact]
    public void RequestFullscreen_HidesThePopoversAndPreventsShowingTheElementAsAPopover()
    {
        var document = Load("<div id=p popover>popover</div><div id=a popover=manual></div>");
        var popover = Get<HtmlElement>(document, "p");
        popover.ShowPopover();
        var element = Get<HtmlElement>(document, "a");

        RequestFullscreen(element);

        Assert.False(popover.Matches(":popover-open"));
        Assert.Throws<DomException>(() => element.ShowPopover());
    }

    [Fact]
    public void RemovingTheFullscreenElement_ExitsFullscreen()
    {
        var document = Load("<div id=a><div id=b></div></div>");
        var events = RecordEvents(document);
        var outer = RequestFullscreen(Get<Element>(document, "a"));
        var inner = RequestFullscreen(Get<Element>(document, "b"));

        // The removed element is no longer in the top layer; the document exits fullscreen from a task
        inner.Remove();
        Assert.Same(outer, document.FullscreenElement);
        Assert.False(inner.Matches(":fullscreen"));
        _contexts.EventLoop.RunUntilIdle();
        Assert.Null(document.FullscreenElement);
        Assert.Equal(["fullscreenchange a", "fullscreenchange b", "fullscreenchange document", "fullscreenchange a"], events);
    }

    [Fact]
    public void FullscreenElement_IsRetargetedOutOfTheShadowTrees()
    {
        var document = Load("<div id=host></div>");
        var host = Get<Element>(document, "host");
        var shadowRoot = host.AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Open });
        shadowRoot.InnerHtml = "<div id=inner></div>";
        var inner = RequestFullscreen(shadowRoot.GetElementById("inner")!);

        Assert.Same(host, document.FullscreenElement);
        Assert.Same(inner, shadowRoot.FullscreenElement);
        Assert.True(host.Matches(":fullscreen"));
        Assert.True(inner.Matches(":fullscreen"));
    }

    [Fact]
    public void RequestFullscreen_InAnIframe_DisplaysTheIframeFullscreen()
    {
        var document = Load("<iframe id=frame srcdoc='<div id=a></div>'></iframe>");
        var iframe = Get<HtmlIFrameElement>(document, "frame");
        var inner = iframe.ContentDocument!;
        var events = new List<string>();
        document.AddEventListener("fullscreenchange", e => events.Add("outer " + ((Element)e.Target!).Id));
        inner.AddEventListener("fullscreenchange", e => events.Add("inner " + ((Element)e.Target!).Id));

        var element = RequestFullscreen(inner.GetElementById("a")!);

        // The events are fired in the order of the documents of the tree
        Assert.Same(iframe, document.FullscreenElement);
        Assert.Same(element, inner.FullscreenElement);
        Assert.Equal(["outer frame", "inner a"], events);
        Assert.Equal("0px", document.DefaultView!.GetComputedStyle(iframe).GetPropertyValue("border-top-width"));

        // Exiting in the document of the iframe exits the containers too
        _ = inner.ExitFullscreen();
        _contexts.EventLoop.RunUntilIdle();
        Assert.Null(document.FullscreenElement);
        Assert.Null(inner.FullscreenElement);
        Assert.Equal(["outer frame", "inner a", "outer frame", "inner a"], events);
    }

    [Fact]
    public void FullscreenEnabled_FollowsThePermissionsPolicy()
    {
        var document = Load("<iframe id=frame src='https://other.example/' ></iframe><iframe id=allowed allowfullscreen src='https://other.example/'></iframe>", new BrowsingContextOptions
        {
            TimeProvider = _clock,
            DocumentLoader = new StaticLoader("<div id=a></div>"),
        });

        var crossOrigin = Get<HtmlIFrameElement>(document, "frame").ContentWindow!.Document;
        var allowed = Get<HtmlIFrameElement>(document, "allowed").ContentWindow!.Document;
        Assert.False(crossOrigin.FullscreenEnabled);
        Assert.True(allowed.FullscreenEnabled);

        Activate(crossOrigin);
        var task = crossOrigin.GetElementById("a")!.RequestFullscreen();
        _contexts.EventLoop.RunUntilIdle();
        Assert.True(task.IsFaulted);
    }

    [Fact]
    public void Escape_FullyExitsFullscreenBeforeTheCloseWatchers()
    {
        var document = Load("<div id=a><div id=b></div></div>");
        var closed = false;
        var watcher = new CloseWatcher(document.DefaultView!);
        watcher.AddEventListener("close", _ => closed = true);
        RequestFullscreen(Get<Element>(document, "a"));
        RequestFullscreen(Get<Element>(document, "b"));

        SendKey(document, "keydown", "Escape");
        _contexts.EventLoop.RunUntilIdle();

        Assert.Null(document.FullscreenElement);
        Assert.False(closed);
    }

    [Fact]
    public void KeyboardLock_EscapeMustBeHeldToExit()
    {
        var document = Load("<div id=a></div>");
        var element = RequestFullscreen(Get<Element>(document, "a"), new FullscreenOptions { KeyboardLock = FullscreenKeyboardLock.Browser });

        // A press of the key is given to the page
        SendKey(document, "keydown", "Escape");
        SendKey(document, "keyup", "Escape");
        _contexts.EventLoop.RunUntilIdle();
        Assert.Same(element, document.FullscreenElement);

        // The key is held: its keydown event repeats
        SendKey(document, "keydown", "Escape");
        _clock.Advance(TimeSpan.FromSeconds(2));
        SendKey(document, "keydown", "Escape");
        _contexts.EventLoop.RunUntilIdle();
        Assert.Null(document.FullscreenElement);
    }

    [Fact]
    public void HostCallbacks_ResizeTheViewport()
    {
        var calls = new List<string>();
        var document = Load("<div id=a></div>", new BrowsingContextOptions
        {
            TimeProvider = _clock,
            EnterFullscreen = (_, options) =>
            {
                calls.Add("enter " + options.NavigationUI);
                return new FullscreenViewportSize(1920, 1080);
            },
            ExitFullscreen = _ => calls.Add("exit"),
        });
        var window = document.DefaultView!;
        var resizes = 0;
        window.AddEventListener("resize", _ => resizes++);
        Assert.Equal(800, window.InnerWidth);

        var element = RequestFullscreen(Get<Element>(document, "a"), new FullscreenOptions { NavigationUI = FullscreenNavigationUI.Hide });
        Assert.Equal(["enter Hide"], calls);
        Assert.Equal((1920, 1080), (window.InnerWidth, window.InnerHeight));
        Assert.Equal(1920, element.GetBoundingClientRect().Width);

        _ = document.ExitFullscreen();
        _clock.Advance(TimeSpan.FromMilliseconds(50));
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal(["enter Hide", "exit"], calls);
        Assert.Equal((800, 600), (window.InnerWidth, window.InnerHeight));
        Assert.Equal(2, resizes);
    }

    [Fact]
    public void Navigation_ExitsFullscreen()
    {
        var exited = false;
        var context = _contexts.Create(new BrowsingContextOptions { TimeProvider = _clock, ExitFullscreen = _ => exited = true });
        var document = context.Navigate("https://example.com/", _ => "<div id=a></div>");
        _contexts.EventLoop.RunUntilIdle();
        RequestFullscreen(Get<Element>(document, "a"));

        context.Navigate("https://example.com/other", _ => "<p>other</p>");
        _contexts.EventLoop.RunUntilIdle();

        Assert.True(exited);
        Assert.Null(context.Document.FullscreenElement);
    }

    private sealed class StaticLoader(string html) : DocumentLoader
    {
        public override ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken)
            => ValueTask.FromResult<DocumentResource?>(new DocumentResource(html) { Headers = [new("Content-Type", "text/html")] });
    }
}
