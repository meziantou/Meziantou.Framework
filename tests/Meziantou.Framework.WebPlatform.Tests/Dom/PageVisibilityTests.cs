using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Tests.Dom;

/// <summary>
/// https://html.spec.whatwg.org/multipage/interaction.html#page-visibility and the system focus of the top-level browsing
/// contexts (https://html.spec.whatwg.org/multipage/interaction.html#tlbc-system-focus).
/// </summary>
public sealed class PageVisibilityTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();

    public void Dispose() => _contexts.Dispose();

    private Document Load(string html)
    {
        var document = _contexts.Create().Navigate("https://example.com/", _ => html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    [Fact]
    public void IsVisible_UpdatesTheVisibilityStateOfTheDocuments()
    {
        var document = Load("<iframe id=frame srcdoc='<p>frame</p>'></iframe>");
        var frameDocument = ((HtmlIFrameElement)document.GetElementById("frame")!).ContentDocument!;
        var events = new List<string>();
        document.AddEventListener("visibilitychange", _ => events.Add("top " + document.VisibilityState));
        frameDocument.AddEventListener("visibilitychange", _ => events.Add("frame " + frameDocument.VisibilityState));
        Assert.True(document.BrowsingContext!.IsVisible);
        Assert.Equal("visible", document.VisibilityState);
        Assert.False(document.Hidden);

        // The documents are updated by tasks of the event loop
        document.BrowsingContext.IsVisible = false;
        Assert.False(frameDocument.BrowsingContext!.IsVisible);
        Assert.Equal("visible", document.VisibilityState);
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal("hidden", document.VisibilityState);
        Assert.True(frameDocument.Hidden);

        document.BrowsingContext.IsVisible = true;
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal(["top hidden", "frame hidden", "top visible", "frame visible"], events);
    }

    [Fact]
    public void IsVisible_QueuesVisibilityStateEntries()
    {
        var document = Load("<p>content</p>");
        var performance = document.DefaultView!.Performance;
        var initial = Assert.Single(performance.GetEntriesByType("visibility-state"));
        Assert.Equal(("visible", 0d, 0d), (initial.Name, initial.StartTime, initial.Duration));

        document.BrowsingContext!.IsVisible = false;
        _contexts.EventLoop.RunUntilIdle();
        var entries = performance.GetEntriesByType("visibility-state");
        Assert.Equal(["visible", "hidden"], entries.Select(entry => entry.Name));
        Assert.IsType<VisibilityStateEntry>(entries[1]);
    }

    [Fact]
    public void IsVisible_NewDocumentsStartHidden()
    {
        var document = Load("<p>content</p>");
        document.BrowsingContext!.IsVisible = false;
        _contexts.EventLoop.RunUntilIdle();

        var frame = (HtmlIFrameElement)document.CreateElement("iframe");
        frame.SetAttribute("srcdoc", "<p>frame</p>");
        document.Body!.AppendChild(frame);
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal("hidden", frame.ContentDocument!.VisibilityState);
        Assert.Equal("hidden", Assert.Single(frame.ContentWindow!.Performance.GetEntriesByType("visibility-state")).Name);
    }

    [Fact]
    public void HasSystemFocus_FiresTheFocusEventsOfTheFocusedElement()
    {
        var document = Load("<input id=a><input id=b>");
        var input = (HtmlElement)document.GetElementById("a")!;
        var other = (HtmlElement)document.GetElementById("b")!;
        var events = new List<string>();
        foreach (var type in new[] { "focus", "blur" })
        {
            input.AddEventListener(type, _ => events.Add("a " + type));
            other.AddEventListener(type, _ => events.Add("b " + type));
            document.DefaultView!.AddEventListener(type, _ => events.Add("window " + type));
        }

        input.Focus();
        Assert.Equal(["a focus"], events);
        Assert.True(document.HasFocus());

        // The window of the host is deactivated: the focused element keeps the focus of the document
        events.Clear();
        document.BrowsingContext!.HasSystemFocus = false;
        Assert.Equal(["a blur", "window blur"], events);
        Assert.False(document.HasFocus());
        Assert.Same(input, document.ActiveElement);

        // The scripts move the focus without focus events
        events.Clear();
        other.Focus();
        Assert.Same(other, document.ActiveElement);
        Assert.Empty(events);

        document.BrowsingContext.HasSystemFocus = true;
        Assert.Equal(["window focus", "b focus"], events);
        Assert.True(document.HasFocus());
    }
}
