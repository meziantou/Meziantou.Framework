using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Html;
using Meziantou.Framework.WebPlatform.Scripting;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Layout;

/// <summary>Resize Observer (https://drafts.csswg.org/resize-observer-1/): the observations are gathered and broadcast by the rendering steps, after the layout.</summary>
public sealed class ResizeObserverTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly FakeTimeProvider _clock = new();
    private readonly List<ScriptError> _errors = [];

    public void Dispose() => _contexts.Dispose();

    /// <summary>Parses a no-quirks document whose body has no margin and the 10px/1 Ahem font, in a 800x600 viewport.</summary>
    private Document Parse(string html, double devicePixelRatio = 1)
    {
        var context = _contexts.Create(new BrowsingContextOptions
        {
            TimeProvider = _clock,
            Viewport = new ViewportOptions { Width = 800, Height = 600, DevicePixelRatio = devicePixelRatio },
            UnhandledScriptError = (_, error) => _errors.Add(error),
        });
        var document = context.Navigate("https://example.com/", _ => "<!DOCTYPE html><style>body { margin: 0; font: 10px/1 Ahem }</style>" + html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    private static HtmlElement Get(Document document, string id) => (HtmlElement)document.GetElementById(id)!;

    /// <summary>Runs the rendering steps of the next frames (the virtual clock advances by one frame each time).</summary>
    private void RunFrames(int count = 1)
    {
        for (var i = 0; i < count; i++)
        {
            _clock.Advance(TimeSpan.FromMilliseconds(17));
            _contexts.EventLoop.RunUntilIdle();
        }
    }

    private static (double Inline, double Block) ToTuple(ResizeObserverSize size) => (size.InlineSize, size.BlockSize);

    [Fact]
    public void Observe_FirstObservationIsDeliveredByTheNextRenderingUpdate()
    {
        var document = Parse("<div id=target style='width: 100px; height: 50px; padding: 5px 10px; border: 2px solid'></div>");
        var target = Get(document, "target");
        var entries = new List<ResizeObserverEntry>();
        var observer = new ResizeObserver((list, _) => entries.AddRange(list));
        observer.Observe(target);
        Assert.Empty(entries);

        RunFrames();
        var entry = Assert.Single(entries);
        Assert.Same(target, entry.Target);
        Assert.Equal((10, 5, 100, 50), (entry.ContentRect.X, entry.ContentRect.Y, entry.ContentRect.Width, entry.ContentRect.Height));
        Assert.Equal((100, 50), ToTuple(Assert.Single(entry.ContentBoxSize)));
        Assert.Equal((124, 64), ToTuple(Assert.Single(entry.BorderBoxSize)));
        Assert.Equal((100, 50), ToTuple(Assert.Single(entry.DevicePixelContentBoxSize)));

        // No notification while the size does not change
        entries.Clear();
        target.Style.SetProperty("margin-left", "20px");
        RunFrames(2);
        Assert.Empty(entries);

        target.Style.SetProperty("width", "80px");
        RunFrames();
        Assert.Equal((80, 50), ToTuple(Assert.Single(Assert.Single(entries).ContentBoxSize)));
    }

    [Fact]
    public void ObservedBox_OnlyItsChangesAreNotified()
    {
        var document = Parse("<div id=target style='box-sizing: border-box; width: 100px; height: 50px; padding: 5px'></div>");
        var target = Get(document, "target");
        var count = 0;
        var observer = new ResizeObserver((_, _) => count++);
        observer.Observe(target, new ResizeObserverOptions { Box = ResizeObserverBoxOptions.BorderBox });
        RunFrames();
        Assert.Equal(1, count);

        // The content box changes, the border box does not
        target.Style.SetProperty("padding", "10px");
        RunFrames();
        Assert.Equal(1, count);

        target.Style.SetProperty("width", "120px");
        RunFrames();
        Assert.Equal(2, count);
    }

    [Fact]
    public void ElementsWithoutBox_AreNotifiedWithAZeroSize()
    {
        var document = Parse("<div id=target style='width: 100px; height: 50px'></div><span id=inline>text</span>");
        var target = Get(document, "target");
        var entries = new List<ResizeObserverEntry>();
        var observer = new ResizeObserver((list, _) => entries.AddRange(list));
        observer.Observe(target);
        observer.Observe(Get(document, "inline"));
        RunFrames();
        Assert.Equal(2, entries.Count);
        Assert.Equal((0, 0), ToTuple(Assert.Single(entries[1].ContentBoxSize)));
        Assert.Equal(0, entries[1].ContentRect.Width);

        entries.Clear();
        target.Style.SetProperty("display", "none");
        RunFrames();
        Assert.Equal((0, 0), ToTuple(Assert.Single(Assert.Single(entries).BorderBoxSize)));

        entries.Clear();
        target.Remove();
        target.Style.SetProperty("display", "block");
        document.Body!.AppendChild(target);
        RunFrames();
        Assert.Equal((100, 50), ToTuple(Assert.Single(Assert.Single(entries).ContentBoxSize)));
    }

    [Fact]
    public void VerticalWritingMode_TheSizesAreLogical()
    {
        var document = Parse("<div id=target style='writing-mode: vertical-rl; width: 100px; height: 50px'></div>");
        ResizeObserverEntry? entry = null;
        var observer = new ResizeObserver((list, _) => entry = list[0]);
        observer.Observe(Get(document, "target"));
        RunFrames();
        Assert.NotNull(entry);
        Assert.Equal((50, 100), ToTuple(entry.ContentBoxSize[0]));
        Assert.Equal((100, 50), (entry.ContentRect.Width, entry.ContentRect.Height));
    }

    [Fact]
    public void DevicePixelContentBox_SnappedToTheDevicePixels()
    {
        var document = Parse("<div style='height: 10.3px'></div><div id=target style='width: 10.5px; height: 10.5px; zoom: 2'></div>", devicePixelRatio: 2);
        ResizeObserverEntry? entry = null;
        var observer = new ResizeObserver((list, _) => entry = list[0]);
        observer.Observe(Get(document, "target"), new ResizeObserverOptions { Box = ResizeObserverBoxOptions.DevicePixelContentBox });
        RunFrames();
        Assert.NotNull(entry);

        // The CSS sizes are not zoomed, the device pixels are: the edges at 10.3 and 31.3 px (zoomed) are snapped to 21 and 63 device px
        Assert.Equal((10.5, 10.5), ToTuple(entry.ContentBoxSize[0]));
        Assert.Equal((42, 42), ToTuple(entry.DevicePixelContentBoxSize[0]));
    }

    [Fact]
    public void MultiColumn_OneSizePerFragment()
    {
        var document = Parse("<div style='column-width: 100px; column-gap: 0; column-fill: auto; width: 200px; height: 100px'><div id=target style='height: 150px'></div></div>");
        ResizeObserverEntry? entry = null;
        var observer = new ResizeObserver((list, _) => entry = list[0]);
        observer.Observe(Get(document, "target"));
        RunFrames();
        Assert.NotNull(entry);
        Assert.Equal([(100d, 100d), (100d, 50d)], entry.ContentBoxSize.Select(ToTuple));
    }

    [Fact]
    public void Broadcast_DeeperTargetsAreDeliveredInTheSameRenderingUpdate()
    {
        var document = Parse("<div id=outer style='height: 50px'><div id=inner></div></div>");
        var inner = Get(document, "inner");
        var notifications = new List<string>();
        var observer = new ResizeObserver((entries, _) =>
        {
            notifications.AddRange(entries.Select(entry => entry.Target.Id));

            // The inner element is deeper than the shallowest target of the broadcast: it is delivered again by the same rendering update
            inner.Style.SetProperty("height", "10px");
        });
        observer.Observe(Get(document, "outer"));
        observer.Observe(inner);
        RunFrames();
        Assert.Equal(["outer", "inner", "inner"], notifications);
        Assert.Empty(_errors);
    }

    [Fact]
    public void ResizeLoop_UndeliveredNotificationsAreReportedAndDeliveredByTheNextRenderingUpdate()
    {
        var document = Parse("<div id=target style='height: 10px'></div>");
        var target = Get(document, "target");
        var count = 0;
        var observer = new ResizeObserver((_, _) =>
        {
            count++;
            target.Style.SetProperty("height", (10 + count) + "px");
        });
        observer.Observe(target);
        RunFrames();
        Assert.Equal(1, count);
        Assert.Equal("ResizeObserver loop completed with undelivered notifications.", Assert.Single(_errors).Message);

        RunFrames();
        Assert.Equal(2, count);
        Assert.Equal(2, _errors.Count);

        observer.Disconnect();
        RunFrames(2);
        Assert.Equal(2, count);
    }

    [Fact]
    public void SkippedContents_AreNotObserved()
    {
        var document = Parse("<div id=container style='content-visibility: hidden'><div id=target style='height: 10px'></div></div>");
        var count = 0;
        var observer = new ResizeObserver((_, _) => count++);
        observer.Observe(Get(document, "target"));
        RunFrames();
        Assert.Equal(0, count);

        Get(document, "container").Style.SetProperty("content-visibility", "visible");
        RunFrames();
        Assert.Equal(1, count);
    }

    [Fact]
    public void UnobserveAndDisconnect_StopTheNotifications()
    {
        var document = Parse("<div id=a></div><div id=b></div>");
        var targets = new List<Element>();
        var observer = new ResizeObserver((entries, _) => targets.AddRange(entries.Select(entry => entry.Target)));
        observer.Observe(Get(document, "a"));
        observer.Observe(Get(document, "b"));
        observer.Unobserve(Get(document, "a"));
        RunFrames();
        Assert.Equal([Get(document, "b")], targets);

        observer.Disconnect();
        Get(document, "b").Style.SetProperty("height", "10px");
        RunFrames();
        Assert.Single(targets);
    }

    [Fact]
    public void Observers_AreNotifiedInTheirOrderOfCreation()
    {
        var document = Parse("<div id=target></div>");
        var order = new List<int>();
        var first = new ResizeObserver((_, _) => order.Add(1));
        var second = new ResizeObserver((_, _) => order.Add(2));
        second.Observe(Get(document, "target"));
        first.Observe(Get(document, "target"));
        RunFrames();
        Assert.Equal([1, 2], order);
    }

    [Fact]
    public void Observe_LaysOutTheDocumentAtEachRenderingUpdate()
    {
        var document = Parse("<div id=target></div>");
        Assert.Null(document.LayoutEngineOrNull);

        var count = 0;
        var observer = new ResizeObserver((_, _) => count++);
        observer.Observe(Get(document, "target"));
        RunFrames();
        Assert.Equal(1, count);
        Assert.True(document.GetLayoutEngine().IsLayoutRequested);
        Assert.False(document.GetLayoutEngine().NeedsLayout);
    }
}
