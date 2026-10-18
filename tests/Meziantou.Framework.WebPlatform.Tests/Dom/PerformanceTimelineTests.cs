using Meziantou.Framework.WebPlatform.Dom;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Dom;

/// <summary>
/// The performance timeline of a window: User Timing (https://w3c.github.io/user-timing/), Navigation Timing
/// (https://w3c.github.io/navigation-timing/), the resource timing of the navigations of the iframes
/// (https://w3c.github.io/resource-timing/) and Server Timing (https://w3c.github.io/server-timing/).
/// </summary>
public sealed class PerformanceTimelineTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly Loader _loader = new();

    public void Dispose() => _contexts.Dispose();

    private BrowsingContext CreateContext(WebPlatformFeatures? features = null)
        => _contexts.Create(new BrowsingContextOptions { DocumentLoader = _loader, TimeProvider = _clock, Features = features });

    private Document Load(string html, WebPlatformFeatures? features = null, string url = "https://example.com/dir/page.html")
    {
        var document = CreateContext(features).Navigate(url, _ => html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    private sealed class Loader : DocumentLoader
    {
        public Dictionary<string, DocumentResource> Resources { get; } = new(StringComparer.Ordinal);

        /// <summary>Called when a request is received, e.g. to advance the clock.</summary>
        public Action? OnRequest { get; set; }

        public override ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken)
        {
            OnRequest?.Invoke();
            return ValueTask.FromResult(Resources.GetValueOrDefault(request.Url));
        }
    }

    private static DocumentResource Html(string html, params KeyValuePair<string, string>[] headers) => new(html) { Headers = [new("Content-Type", "text/html"), .. headers] };

    private static DocumentResource Redirect(string location, params KeyValuePair<string, string>[] headers) => new("") { StatusCode = 302, Headers = [new("Location", location), .. headers] };

    [Fact]
    public void Mark_IsAddedToTheTimeline()
    {
        var performance = Load("").DefaultView!.Performance;
        _clock.Advance(TimeSpan.FromMilliseconds(10));

        var first = performance.Mark("a");
        var second = performance.Mark("a", new PerformanceMarkOptions { StartTime = 3, Detail = "data" });
        performance.Mark("b");

        Assert.Equal("mark", first.EntryType);
        Assert.Equal(10, first.StartTime);
        Assert.Equal(0, first.Duration);
        Assert.Null(first.Detail);
        Assert.Equal(3, second.StartTime);
        Assert.Equal("data", second.Detail);
        Assert.Equal([second, first], performance.GetEntriesByName("a", "mark"));
        Assert.Equal(3, performance.GetEntriesByType("mark").Count);

        performance.ClearMarks("a");
        Assert.Equal(["b"], performance.GetEntriesByType("mark").Select(entry => entry.Name));
        performance.ClearMarks();
        Assert.Empty(performance.GetEntriesByType("mark"));
    }

    [Fact]
    public void Mark_Constructor_DoesNotAddTheMarkToTheTimeline()
    {
        var window = Load("").DefaultView!;

        var mark = new PerformanceMark(window, "a");

        Assert.Equal("a", mark.Name);
        Assert.Empty(window.Performance.GetEntriesByType("mark"));
    }

    [Fact]
    public void Mark_InvalidArguments_Throw()
    {
        var performance = Load("").DefaultView!.Performance;

        Assert.Throws<TypeErrorException>(() => performance.Mark("a", new PerformanceMarkOptions { StartTime = -1 }));
        Assert.Equal("SyntaxError", Assert.Throws<DomException>(() => performance.Mark("domComplete")).Name);
    }

    [Fact]
    public void Measure_ComputesTheTimeBetweenTwoMarks()
    {
        var performance = Load("").DefaultView!.Performance;
        performance.Mark("start", new PerformanceMarkOptions { StartTime = 5 });
        performance.Mark("end", new PerformanceMarkOptions { StartTime = 12 });
        _clock.Advance(TimeSpan.FromMilliseconds(20));

        var betweenMarks = performance.Measure("m1", "start", "end");
        var fromTheTimeOrigin = performance.Measure("m2");
        var withOptions = performance.Measure("m3", new PerformanceMeasureOptions { Start = "start", Duration = 2, Detail = 42 });
        var withTimes = performance.Measure("m4", new PerformanceMeasureOptions { Start = 1d, End = "end" });

        Assert.Equal("measure", betweenMarks.EntryType);
        Assert.Equal((5, 7), (betweenMarks.StartTime, betweenMarks.Duration));
        Assert.Equal((0, 20), (fromTheTimeOrigin.StartTime, fromTheTimeOrigin.Duration));
        Assert.Equal((5, 2, 42), (withOptions.StartTime, withOptions.Duration, withOptions.Detail));
        Assert.Equal((1, 11), (withTimes.StartTime, withTimes.Duration));
        Assert.Equal(["m2", "m4", "m1", "m3"], performance.GetEntriesByType("measure").Select(entry => entry.Name));

        performance.ClearMeasures("m1");
        Assert.Equal(3, performance.GetEntriesByType("measure").Count);
        performance.ClearMeasures();
        Assert.Empty(performance.GetEntriesByType("measure"));
    }

    [Fact]
    public void Measure_InvalidArguments_Throw()
    {
        var performance = Load("").DefaultView!.Performance;
        performance.Mark("a");

        Assert.Equal("SyntaxError", Assert.Throws<DomException>(() => performance.Measure("m", "missing")).Name);
        Assert.Throws<TypeErrorException>(() => performance.Measure("m", new PerformanceMeasureOptions { Start = "a" }, "a"));
        Assert.Throws<TypeErrorException>(() => performance.Measure("m", new PerformanceMeasureOptions { Duration = 1 }));
        Assert.Throws<TypeErrorException>(() => performance.Measure("m", new PerformanceMeasureOptions { Start = 1d, Duration = 1, End = 2d }));
        Assert.Throws<TypeErrorException>(() => performance.Measure("m", new PerformanceMeasureOptions { Start = -1d }));
    }

    [Fact]
    public void Measure_FromANavigationTimingAttribute()
    {
        var document = CreateContext().Navigate("https://example.com/", _ => "");
        var performance = document.DefaultView!.Performance;

        // The load event is not fired yet
        Assert.Equal("InvalidAccessError", Assert.Throws<DomException>(() => performance.Measure("m", "navigationStart", "loadEventEnd")).Name);

        // Its time is relative to the start of the navigation
        _clock.Advance(TimeSpan.FromMilliseconds(4));
        _contexts.EventLoop.RunUntilIdle();
        var measure = performance.Measure("m", "navigationStart", "loadEventEnd");

        Assert.Equal((0, 4), (measure.StartTime, measure.Duration));
    }

    [Fact]
    public void PerformanceObserver_ReceivesTheMarksAndTheMeasures()
    {
        var window = Load("").DefaultView!;
        var names = new List<string>();
        var observer = new PerformanceObserver(window, (entries, _, _) => names.AddRange(entries.GetEntries().Select(entry => entry.EntryType + ":" + entry.Name)));
        window.Performance.Mark("before");
        observer.Observe(new PerformanceObserverInit { EntryTypes = ["mark", "measure"] });

        window.Performance.Mark("a");
        window.Performance.Measure("m", "a");
        Assert.Empty(names);
        _contexts.EventLoop.RunUntilIdle();

        Assert.Equal(["mark:a", "measure:m"], names);
        Assert.Equal(["mark", "measure", "navigation", "resource"], window.Performance.SupportedEntryTypes.Where(type => type is "mark" or "measure" or "navigation" or "resource"));
    }

    [Fact]
    public void NavigationTiming_IsTheEntryOfTheDocument()
    {
        var context = CreateContext();
        var document = context.Navigate("https://example.com/dir/page.html", _ =>
        {
            _clock.Advance(TimeSpan.FromMilliseconds(5));
            return new DocumentResource("<p>content</p>") { Headers = [new("Content-Type", "text/html"), new("Server-Timing", "db;dur=53.2;desc=\"Database, primary\", cache")] };
        });
        var performance = document.DefaultView!.Performance;
        var entry = (PerformanceNavigationTiming)Assert.Single(performance.GetEntriesByType("navigation"));

        // The entry is available while the document is loading: the times of the load are not set yet
        Assert.Equal("https://example.com/dir/page.html", entry.Name);
        Assert.Equal("navigation", entry.InitiatorType);
        Assert.Equal(NavigationTimingType.Navigate, entry.Type);
        Assert.Equal((0, 0, 0), (entry.StartTime, entry.FetchStart, entry.Duration));
        Assert.Equal((5, 5), (entry.ResponseStart, entry.ResponseEnd));
        Assert.Equal((0, 0), (entry.DomComplete, entry.LoadEventEnd));
        Assert.Equal(14ul, entry.DecodedBodySize);
        Assert.Equal(200, entry.ResponseStatus);
        Assert.Equal("text/html", entry.ContentType);
        Assert.Equal("http/1.1", entry.NextHopProtocol);
        Assert.Equal([("db", 53.2, "Database, primary"), ("cache", 0, "")], entry.ServerTiming.Select(metric => (metric.Name, metric.Duration, metric.Description)));

        var observed = new List<PerformanceEntry>();
        new PerformanceObserver(document.DefaultView, (entries, _, _) => observed.AddRange(entries.GetEntries())).Observe(new PerformanceObserverInit { Type = "navigation" });
        _clock.Advance(TimeSpan.FromMilliseconds(3));
        _contexts.EventLoop.RunUntilIdle();

        Assert.Equal((5, 8, 8), (entry.DomInteractive, entry.DomComplete, entry.LoadEventEnd));
        Assert.Equal(8, entry.Duration);
        Assert.Same(entry, Assert.Single(observed));
        Assert.Same(entry, performance.GetEntries()[0]);

        // The legacy interfaces expose the same times, in milliseconds since the Unix epoch
        var timing = performance.Timing!;
        Assert.Equal((ulong)new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(), timing.NavigationStart);
        Assert.Equal(timing.NavigationStart + 5, timing.ResponseEnd);
        Assert.Equal(timing.NavigationStart + 8, timing.LoadEventEnd);
        Assert.Equal(0ul, timing.UnloadEventStart);
        Assert.Equal((PerformanceNavigation.TypeNavigate, 0), (performance.Navigation!.Type, performance.Navigation.RedirectCount));
    }

    [Theory]
    [InlineData("https://example.com/redirect", null, 1)]
    [InlineData("https://other.example/redirect", null, 0)]
    [InlineData("https://other.example/redirect", "*", 1)]
    [InlineData("https://other.example/redirect", "https://example.com", 1)]
    [InlineData("https://other.example/redirect", "https://another.example", 0)]
    public void NavigationTiming_ExposesTheRedirectsOfTheOriginOrAllowedByTimingAllowOrigin(string url, string? timingAllowOrigin, int expectedRedirectCount)
    {
        _loader.Resources[url] = timingAllowOrigin is null ? Redirect("https://example.com/final") : Redirect("https://example.com/final", new KeyValuePair<string, string>("Timing-Allow-Origin", timingAllowOrigin));
        _loader.Resources["https://example.com/final"] = Html("final");
        var context = CreateContext();

        context.Navigate(url);
        _contexts.EventLoop.RunUntilIdle();

        Assert.Equal("https://example.com/final", context.Document.Url);
        var entry = (PerformanceNavigationTiming)Assert.Single(context.Document.DefaultView!.Performance.GetEntriesByType("navigation"));
        Assert.Equal(expectedRedirectCount, entry.RedirectCount);
    }

    [Fact]
    public void NavigationTiming_Reload_HasTheReloadType()
    {
        _loader.Resources["https://example.com/page"] = Html("page");
        var context = CreateContext();
        context.Navigate("https://example.com/page");
        _contexts.EventLoop.RunUntilIdle();

        _loader.OnRequest = () => _clock.Advance(TimeSpan.FromMilliseconds(1));
        context.Document.DefaultView!.Location.Reload();
        _contexts.EventLoop.RunUntilIdle();

        var performance = context.Document.DefaultView!.Performance;
        Assert.Equal(NavigationTimingType.Reload, ((PerformanceNavigationTiming)performance.GetEntriesByType("navigation")[0]).Type);
        Assert.Equal(PerformanceNavigation.TypeReload, performance.Navigation!.Type);

        // The previous document has the same origin: the times of its unload event are exposed
        Assert.NotEqual(0ul, performance.Timing!.UnloadEventStart);
    }

    [Fact]
    public void Iframe_SameOrigin_ReportsItsNavigationToTheParent()
    {
        _loader.Resources["https://example.com/dir/frame.html"] = Html("frame");
        var document = Load("<iframe src=frame.html></iframe>");

        var entry = (PerformanceResourceTiming)Assert.Single(document.DefaultView!.Performance.GetEntriesByType("resource"));
        Assert.Equal("https://example.com/dir/frame.html", entry.Name);
        Assert.Equal("iframe", entry.InitiatorType);
        Assert.Equal(200, entry.ResponseStatus);
        Assert.Equal("text/html", entry.ContentType);
        Assert.Equal(305ul, entry.TransferSize);
    }

    [Fact]
    public void Iframe_CrossOrigin_ReportsAnOpaqueEntryWhenItIsLoaded()
    {
        _loader.Resources["https://other.example/frame.html"] = Html("frame");
        var document = Load("<iframe src=https://other.example/frame.html></iframe>");

        var entry = (PerformanceResourceTiming)Assert.Single(document.DefaultView!.Performance.GetEntriesByType("resource"));
        Assert.Equal("https://other.example/frame.html", entry.Name);
        Assert.Equal("iframe", entry.InitiatorType);
        Assert.Equal(0, entry.ResponseStatus);
        Assert.Equal(0ul, entry.TransferSize);
        Assert.Equal("", entry.NextHopProtocol);
    }

    [Fact]
    public void Iframe_CrossOriginWithTimingAllowOrigin_ExposesItsTimingButNotItsStatus()
    {
        _loader.Resources["https://other.example/frame.html"] = Html("frame", new KeyValuePair<string, string>("Timing-Allow-Origin", "https://example.com"));
        var document = Load("<iframe src=https://other.example/frame.html></iframe>");

        var entry = (PerformanceResourceTiming)Assert.Single(document.DefaultView!.Performance.GetEntriesByType("resource"));
        Assert.Equal("http/1.1", entry.NextHopProtocol);
        Assert.Equal(305ul, entry.TransferSize);
        Assert.Equal(0, entry.ResponseStatus);
        Assert.Equal("", entry.ContentType);
    }

    [Fact]
    public void DisabledFeatures_AreNotInTheTimeline()
    {
        var performance = Load("", new WebPlatformFeatures { UserTimingEnabled = false, NavigationTimingEnabled = false, VisibilityStateEntryEnabled = false }).DefaultView!.Performance;

        Assert.Equal(["resource"], performance.SupportedEntryTypes.Where(type => type is "mark" or "measure" or "navigation" or "resource" or "visibility-state"));
        Assert.Empty(performance.GetEntries());
    }

    [Theory]
    [InlineData("a", "a", 0d, "")]
    [InlineData("a;dur=1.5", "a", 1.5, "")]
    [InlineData("a;desc=text;dur=2", "a", 2d, "text")]
    [InlineData("a ; DUR = 3 ; Desc = \"quoted \\\" text\"", "a", 3d, "quoted \" text")]
    [InlineData("a;dur=1;dur=2;other=x", "a", 1d, "")]
    [InlineData("a;dur=invalid", "a", 0d, "")]
    public void ServerTiming_Metric_IsParsed(string header, string name, double duration, string description)
    {
        var metric = Assert.Single(PerformanceServerTiming.Parse([new("Server-Timing", header)]));

        Assert.Equal((name, duration, description), (metric.Name, metric.Duration, metric.Description));
    }

    [Fact]
    public void ServerTiming_SeveralHeaders_AreCombined()
    {
        var metrics = PerformanceServerTiming.Parse([new("Server-Timing", "a, b;dur=1, ;dur=2"), new("Content-Type", "text/html"), new("server-timing", "c;desc=\"x, y\"")]);

        Assert.Equal(["a", "b", "c"], metrics.Select(metric => metric.Name));
        Assert.Equal("x, y", metrics[2].Description);
    }
}
