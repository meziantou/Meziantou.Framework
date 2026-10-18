using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Html;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Layout;

/// <summary>Intersection Observer (https://w3c.github.io/IntersectionObserver/) and the lazy loading of the img and iframe elements: the observations are computed by the rendering steps, after the layout.</summary>
public sealed class IntersectionObserverTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly FakeTimeProvider _clock = new();
    private readonly ImageLoader _loader = new();

    public void Dispose() => _contexts.Dispose();

    /// <summary>Parses a no-quirks document whose body has no margin, in a 800x600 viewport without scrollbars (when the document fits).</summary>
    private Document Parse(string html, bool scriptingEnabled = false, WebPlatformFeatures? features = null)
    {
        var context = _contexts.Create(new BrowsingContextOptions
        {
            TimeProvider = _clock,
            Viewport = new ViewportOptions { Width = 800, Height = 600 },
            DocumentLoader = _loader,
            ScriptingEnabled = scriptingEnabled,
            Features = features,
        });
        var document = context.Navigate("https://example.com/", _ => "<!DOCTYPE html><style>body { margin: 0 }</style>" + html);
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

    private static (double Left, double Top, double Width, double Height) ToTuple(DomRectReadOnly? rect) => rect is null ? default : (rect.X, rect.Y, rect.Width, rect.Height);

    [Fact]
    public void ImplicitRoot_TheIntersectionWithTheViewportIsNotifiedWhenItChanges()
    {
        var document = Parse("<div style='height: 700px'></div><div id=target style='width: 100px; height: 100px'></div><div style='height: 1000px'></div>");
        var target = Get(document, "target");
        var entries = new List<IntersectionObserverEntry>();
        var observer = new IntersectionObserver((list, _) => entries.AddRange(list));
        observer.Observe(target);
        Assert.Empty(entries);

        // The initial observation: below the viewport (whose vertical scrollbar is 15px wide)
        RunFrames();
        var entry = Assert.Single(entries);
        Assert.Same(target, entry.Target);
        Assert.False(entry.IsIntersecting);
        Assert.Equal(0, entry.IntersectionRatio);
        Assert.Equal((0, 700, 100, 100), ToTuple(entry.BoundingClientRect));
        Assert.Equal((0, 0, 0, 0), ToTuple(entry.IntersectionRect));
        Assert.Equal((0, 0, 785, 600), ToTuple(entry.RootBounds));

        // Nothing changes: no new entry
        entries.Clear();
        RunFrames(2);
        Assert.Empty(entries);

        document.ScrollingElement!.ScrollTop = 150;
        RunFrames();
        entry = Assert.Single(entries);
        Assert.True(entry.IsIntersecting);
        Assert.Equal(0.5, entry.IntersectionRatio);
        Assert.Equal((0, 550, 100, 100), ToTuple(entry.BoundingClientRect));
        Assert.Equal((0, 550, 100, 50), ToTuple(entry.IntersectionRect));

        // The ratio changes, but no threshold is crossed
        entries.Clear();
        document.ScrollingElement!.ScrollTop = 200;
        RunFrames();
        Assert.Empty(entries);
    }

    [Fact]
    public void ExplicitRoot_ClipsByTheScrollportAndAppliesTheRootMargin()
    {
        var document = Parse("""
            <div id=root style='width: 200px; height: 100px; overflow: hidden; border: 5px solid'>
              <div style='height: 120px'></div>
              <div id=target style='width: 50px; height: 50px'></div>
            </div>
            """);
        var entries = new List<IntersectionObserverEntry>();
        var root = Get(document, "root");
        var observer = new IntersectionObserver((list, _) => entries.AddRange(list), new IntersectionObserverInit { Root = root, RootMargin = "10px 0px 30% 0px", Threshold = new List<double> { 0, 1 } });
        Assert.Same(root, observer.Root);
        Assert.Equal("10px 0px 30% 0px", observer.RootMargin);
        observer.Observe(Get(document, "target"));
        RunFrames();

        // The root intersection rectangle is the padding box (5..105) expanded by 10px above and 30% of its height below
        var entry = Assert.Single(entries);
        Assert.Equal((5, -5, 200, 140), ToTuple(entry.RootBounds));
        Assert.True(entry.IsIntersecting);
        Assert.Equal((5, 125, 50, 10), ToTuple(entry.IntersectionRect));
        Assert.Equal(0.2, entry.IntersectionRatio, 6);

        entries.Clear();
        root.ScrollTop = 120;
        RunFrames();
        Assert.Equal(1, Assert.Single(entries).IntersectionRatio);
    }

    [Fact]
    public void ScrollMargin_ExpandsTheScrollportsOfTheAncestors()
    {
        var document = Parse("""
            <div id=scroller style='width: 100px; height: 100px; overflow: scroll; scrollbar-width: none'>
              <div style='height: 110px'></div>
              <div id=target style='width: 50px; height: 50px'></div>
            </div>
            """);
        var withMargin = new List<IntersectionObserverEntry>();
        var withoutMargin = new List<IntersectionObserverEntry>();
        new IntersectionObserver((list, _) => withMargin.AddRange(list), new IntersectionObserverInit { ScrollMargin = "20px" }).Observe(Get(document, "target"));
        new IntersectionObserver((list, _) => withoutMargin.AddRange(list)).Observe(Get(document, "target"));
        RunFrames();

        Assert.True(Assert.Single(withMargin).IsIntersecting);
        Assert.Equal((0, 110, 50, 10), ToTuple(withMargin[0].IntersectionRect));
        Assert.False(Assert.Single(withoutMargin).IsIntersecting);
    }

    [Fact]
    public void EdgeAdjacentAndZeroAreaIntersections_AreIntersecting()
    {
        var document = Parse("""
            <div id=root style='width: 200px; height: 200px; overflow: hidden'>
              <div id=target style='width: 100px; height: 100px; transform: translateY(200px)'></div>
              <div id=empty style='width: 0; height: 0'></div>
            </div>
            """);
        var entries = new List<IntersectionObserverEntry>();
        var observer = new IntersectionObserver((list, _) => entries.AddRange(list), new IntersectionObserverInit { Root = Get(document, "root") });
        observer.Observe(Get(document, "target"));
        observer.Observe(Get(document, "empty"));
        RunFrames();

        Assert.Equal(2, entries.Count);
        Assert.True(entries[0].IsIntersecting);
        Assert.Equal((0, 200, 100, 0), ToTuple(entries[0].IntersectionRect));
        Assert.Equal(0, entries[0].IntersectionRatio);
        Assert.True(entries[1].IsIntersecting);
        Assert.Equal(1, entries[1].IntersectionRatio);
    }

    [Fact]
    public void Thresholds_AnEntryIsQueuedWhenAThresholdIsCrossed()
    {
        var document = Parse("<div style='height: 550px'></div><div id=target style='width: 100px; height: 100px'></div>");
        var entries = new List<IntersectionObserverEntry>();
        var observer = new IntersectionObserver((list, _) => entries.AddRange(list), new IntersectionObserverInit { Threshold = new List<double> { 1, 0.75 } });
        Assert.Equal([0.75, 1], observer.Thresholds);
        observer.Observe(Get(document, "target"));
        RunFrames();

        // A ratio below the first threshold is not intersecting (w3c/IntersectionObserver#432), but the rectangle is reported
        var entry = Assert.Single(entries);
        Assert.False(entry.IsIntersecting);
        Assert.Equal(0.5, entry.IntersectionRatio);
        Assert.Equal(50, entry.IntersectionRect.Height);

        entries.Clear();
        Get(document, "target").Style.SetProperty("margin-top", "-20px");
        RunFrames();
        Assert.Empty(entries);

        Get(document, "target").Style.SetProperty("margin-top", "-50px");
        RunFrames();
        entry = Assert.Single(entries);
        Assert.True(entry.IsIntersecting);
        Assert.Equal(1, entry.IntersectionRatio);
    }

    [Fact]
    public void Constructor_ParsesTheMarginsAndValidatesTheThresholds()
    {
        var observer = new IntersectionObserver((_, _) => { }, new IntersectionObserverInit { RootMargin = "1in 10%", ScrollMargin = "  " });
        Assert.Equal("96px 10% 96px 10%", observer.RootMargin);
        Assert.Equal("0px 0px 0px 0px", observer.ScrollMargin);
        Assert.Equal([0d], observer.Thresholds);
        Assert.Equal("377px 377px 377px 377px", new IntersectionObserver((_, _) => { }, new IntersectionObserverInit { RootMargin = "10cm" }).RootMargin);

        foreach (var margin in (string[])["1", "2em", "auto", "calc(1px + 2px)", "1px 1px 1px 1px 1px"])
        {
            Assert.Equal("SyntaxError", Assert.Throws<DomException>(() => new IntersectionObserver((_, _) => { }, new IntersectionObserverInit { RootMargin = margin })).Name);
        }

        Assert.Throws<RangeErrorException>(() => new IntersectionObserver((_, _) => { }, new IntersectionObserverInit { Threshold = 1.5 }));
        Assert.Throws<RangeErrorException>(() => new IntersectionObserver((_, _) => { }, new IntersectionObserverInit { Threshold = new List<double> { 0, double.NaN } }));

        // trackVisibility forces a delay of at least 100ms
        Assert.Equal(100, new IntersectionObserver((_, _) => { }, new IntersectionObserverInit { TrackVisibility = true }).Delay);
    }

    [Fact]
    public void NotRendered_TheRectanglesAreZeroRectangles()
    {
        var document = Parse("<div id=target style='width: 100px; height: 100px'></div>");
        var target = Get(document, "target");
        var entries = new List<IntersectionObserverEntry>();
        var observer = new IntersectionObserver((list, _) => entries.AddRange(list));
        observer.Observe(target);
        RunFrames();
        Assert.True(Assert.Single(entries).IsIntersecting);

        entries.Clear();
        target.Style.SetProperty("display", "none");
        RunFrames();
        var entry = Assert.Single(entries);
        Assert.False(entry.IsIntersecting);
        Assert.Equal((0, 0, 0, 0), ToTuple(entry.BoundingClientRect));
        Assert.Equal((0, 0, 0, 0), ToTuple(entry.RootBounds));
    }

    [Fact]
    public void ClipPath_ClipsTheDescendants()
    {
        var document = Parse("<div id=container style='width: 100px; height: 100px'><div id=target style='width: 100px; height: 100px'></div></div>");
        var entries = new List<IntersectionObserverEntry>();
        new IntersectionObserver((list, _) => entries.AddRange(list), new IntersectionObserverInit { Threshold = new List<double> { 0, 0.5, 1 } }).Observe(Get(document, "target"));
        RunFrames();
        Assert.Equal(1, Assert.Single(entries).IntersectionRatio);

        entries.Clear();
        Get(document, "container").Style.SetProperty("clip-path", "inset(25px 0 0 50px)");
        RunFrames();
        var entry = Assert.Single(entries);
        Assert.Equal((50, 25, 50, 75), ToTuple(entry.IntersectionRect));
        Assert.Equal(0.375, entry.IntersectionRatio);
    }

    [Fact]
    public void NestedBrowsingContext_TheTargetIsClippedByTheViewportsOfTheAncestors()
    {
        var document = Parse("<div style='height: 550px'></div><iframe id=frame style='border: 0; width: 200px; height: 200px' srcdoc=\"<body style='margin: 0'><div id=target style='width: 50px; height: 100px'></div>\"></iframe>");
        var frame = (HtmlIFrameElement)Get(document, "frame");
        var target = frame.ContentDocument!.GetElementById("target")!;
        var entries = new List<IntersectionObserverEntry>();
        new IntersectionObserver((list, _) => entries.AddRange(list)).Observe(target);
        RunFrames();

        // The iframe is at y = 550 in the viewport of the top-level document (600px high): 50px of the target are visible.
        // The intersection is in the coordinates of the viewport of the iframe, the root bounds in the ones of the top-level document.
        var entry = Assert.Single(entries);
        Assert.True(entry.IsIntersecting);
        Assert.Equal((0, 0, 50, 100), ToTuple(entry.BoundingClientRect));
        Assert.Equal((0, 0, 50, 50), ToTuple(entry.IntersectionRect));
        Assert.Equal(600, entry.RootBounds!.Height);

        entries.Clear();
        frame.Style.SetProperty("margin-top", "60px");
        RunFrames();
        Assert.False(Assert.Single(entries).IsIntersecting);
    }

    [Fact]
    public void TakeRecordsAndDisconnect()
    {
        var document = Parse("<div id=target style='width: 100px; height: 100px'></div>");
        var entries = new List<IntersectionObserverEntry>();
        var observer = new IntersectionObserver((list, _) => entries.AddRange(list));
        observer.Observe(Get(document, "target"));

        // The rendering update queues the entry, a task delivers it
        _clock.Advance(TimeSpan.FromMilliseconds(17));
        _contexts.EventLoop.RunUntilIdle();
        Assert.Single(entries);

        entries.Clear();
        observer.Unobserve(Get(document, "target"));
        Get(document, "target").Style.SetProperty("display", "none");
        RunFrames();
        Assert.Empty(entries);

        observer.Observe(Get(document, "target"));
        observer.Disconnect();
        RunFrames();
        Assert.Empty(entries);
        Assert.Empty(observer.TakeRecords());
    }

    [Fact]
    public void TrackVisibility_AnOccludingBoxMakesTheTargetNotVisible()
    {
        var document = Parse("<div id=target style='width: 100px; height: 100px; background: green'></div><div id=occluder style='position: relative; width: 100px; height: 100px; background: blue'></div>");
        var entries = new List<IntersectionObserverEntry>();
        new IntersectionObserver((list, _) => entries.AddRange(list), new IntersectionObserverInit { TrackVisibility = true }).Observe(Get(document, "target"));
        RunFrames();
        Assert.True(Assert.Single(entries).IsVisible);

        entries.Clear();
        Get(document, "occluder").Style.SetProperty("margin-top", "-10px");
        RunFrames(8);
        var entry = Assert.Single(entries);
        Assert.True(entry.IsIntersecting);
        Assert.False(entry.IsVisible);

        entries.Clear();
        Get(document, "occluder").Style.SetProperty("margin-top", "0");
        Get(document, "target").Style.SetProperty("opacity", "0.5");
        RunFrames(8);
        Assert.Empty(entries);
    }

    [Fact]
    public void LazyImages_AreFetchedWhenTheyAreCloseToTheViewport()
    {
        var document = Parse("""
            <img id=near loading=lazy src=near.png>
            <div style='height: 3000px'></div>
            <img id=far loading=lazy src=far.png>
            <img id=eager src=eager.png>
            """, scriptingEnabled: true);
        RunFrames();
        Assert.Contains("https://example.com/near.png", _loader.Requests);
        Assert.Contains("https://example.com/eager.png", _loader.Requests);
        Assert.DoesNotContain("https://example.com/far.png", _loader.Requests);

        // 1250px of the viewport: the image is fetched before it is scrolled into view
        document.ScrollingElement!.ScrollTop = 1800;
        RunFrames(2);
        Assert.Contains("https://example.com/far.png", _loader.Requests);
    }

    [Fact]
    public void LazyImages_ChangingTheAttributeToEagerFetchesTheImage()
    {
        var document = Parse("<div style='height: 5000px'></div><img id=image loading=lazy src=image.png>", scriptingEnabled: true);
        RunFrames();
        Assert.DoesNotContain("https://example.com/image.png", _loader.Requests);

        Get(document, "image").SetAttribute("loading", "eager");
        _contexts.EventLoop.RunUntilIdle();
        Assert.Contains("https://example.com/image.png", _loader.Requests);
    }

    [Fact]
    public void LazyImages_LoadedAtOnceWhenTheIntersectionObserversAreDisabledOrWithoutScripting()
    {
        Parse("<div style='height: 5000px'></div><img loading=lazy src=disabled.png>", scriptingEnabled: true, features: new WebPlatformFeatures { IntersectionObserverEnabled = false });
        Parse("<div style='height: 5000px'></div><img loading=lazy src=noscript.png>", scriptingEnabled: false);
        Assert.Contains("https://example.com/disabled.png", _loader.Requests);
        Assert.Contains("https://example.com/noscript.png", _loader.Requests);
    }

    [Fact]
    public void LazyIframes_NavigateWhenTheyAreCloseToTheViewport()
    {
        var document = Parse("<div style='height: 5000px'></div><iframe id=frame loading=lazy src=frame.html></iframe>", scriptingEnabled: true);
        var frame = (HtmlIFrameElement)Get(document, "frame");
        RunFrames();
        Assert.DoesNotContain("https://example.com/frame.html", _loader.Requests);
        Assert.Equal("about:blank", frame.ContentDocument!.Url);

        _ = frame.ScrollIntoView();
        RunFrames(2);
        Assert.Contains("https://example.com/frame.html", _loader.Requests);
    }

    /// <summary>Records the URLs of the requests: the images are 1x1 GIF images, the documents are empty.</summary>
    private sealed class ImageLoader : DocumentLoader
    {
        private static readonly byte[] Gif = Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7");

        public List<string> Requests { get; } = [];

        public override ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request.Url);
            return ValueTask.FromResult<DocumentResource?>(request.Destination == RequestDestination.Image
                ? new DocumentResource(Gif) { Headers = [new("Content-Type", "image/gif")] }
                : new DocumentResource("<!DOCTYPE html>") { Headers = [new("Content-Type", "text/html")] });
        }
    }
}
