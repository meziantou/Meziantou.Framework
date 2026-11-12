using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Tests.Scripting;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.JavaScript;

/// <summary>The performance timeline used by the scripts: User Timing, Navigation Timing, Long Tasks and the performance observers.</summary>
public sealed class PerformanceTimelineScriptTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly List<ScriptError> _errors = [];
    private readonly RecordingLoader _loader = new();
    private readonly ScriptEngine _engine = TestScriptEngines.Create();

    public void Dispose()
    {
        _contexts.Dispose();
        TestScriptEngines.Dispose(_engine);
    }

    // Each read of the clock advances it by 1 ms: a script that waits for 60 ms of performance.now() runs for 60 ms
    private readonly FakeTimeProvider _clock = new() { AutoAdvanceAmount = TimeSpan.FromMilliseconds(1) };

    private Document Load(string html)
    {
        var context = _contexts.Create(new BrowsingContextOptions
        {
            ScriptEngine = _engine,
            DocumentLoader = _loader,
            TimeProvider = _clock,
            UnhandledScriptError = (_, error) => _errors.Add(error),
        });

        var document = context.Navigate("https://example.com/dir/page.html", _ => html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    [Fact]
    public void Mark_Detail_IsAStructuredClone()
    {
        var document = Load("""
            <script>
              const detail = { value: 1 };
              const mark = performance.mark('a', { detail, startTime: 0 });
              detail.value = 2;
              const measure = performance.measure('m', { start: 'a', duration: 3, detail: [1, 2] });
              let error = '';
              try { new PerformanceMark('b', { detail: Symbol() }); } catch (e) { error = e.name; }
              document.title = JSON.stringify([
                mark instanceof PerformanceMark, mark.detail.value, mark.detail === mark.detail, performance.mark('c').detail, mark.toJSON().startTime,
                measure instanceof PerformanceMeasure, measure.detail, measure.startTime, measure.duration, error,
                performance.getEntriesByType('mark').map(entry => entry.name),
              ]);
            </script>
            """);

        Assert.Equal("""[true,1,true,null,0,true,[1,2],0,3,"DataCloneError",["a","c"]]""", document.Title);
        Assert.Empty(_errors);
    }

    [Fact]
    public void PerformanceObserver_CallbackThisValue_IsTheObserver()
    {
        var document = Load("""
            <script>
              const observer = new PerformanceObserver(function (list, argument) {
                document.title = [this === observer, argument === observer, list.getEntries().map(entry => entry.entryType + ':' + entry.name)].join();
              });
              observer.observe({ entryTypes: ['mark', 'measure'] });
              performance.mark('a');
              performance.measure('m');
            </script>
            """);

        Assert.Equal("true,true,measure:m,mark:a", document.Title);
        Assert.Empty(_errors);
    }

    [Fact]
    public void NavigationTiming_IsExposedToTheScripts()
    {
        var document = Load("""
            <script>
              const entry = performance.getEntriesByType('navigation')[0];
              window.onload = () => setTimeout(() => {
                const json = entry.toJSON();
                document.title = JSON.stringify([
                  entry instanceof PerformanceNavigationTiming, entry instanceof PerformanceResourceTiming, entry.name, entry.type, entry.startTime,
                  entry.duration === entry.loadEventEnd, json.type, json.domComplete === entry.domComplete, Array.isArray(entry.serverTiming),
                  performance.timing instanceof PerformanceTiming, performance.timing === performance.timing, performance.timing.loadEventEnd >= performance.timing.navigationStart,
                  performance.navigation.type === PerformanceNavigation.TYPE_NAVIGATE, Object.keys(performance.toJSON()).sort(),
                ]);
              });
            </script>
            """);

        Assert.Equal("""[true,true,"https://example.com/dir/page.html","navigate",0,true,"navigate",true,true,true,true,true,true,["interactionCount","navigation","timeOrigin","timing"]]""", document.Title);
        Assert.Empty(_errors);
    }

    private const string LongTaskScript = "const begin = performance.now(); while (performance.now() < begin + 60);";

    [Fact]
    public void LongTask_IsReportedToTheObservers()
    {
        var document = Load($$"""
            <script>
              const results = [];
              new PerformanceObserver(list => {
                for (const entry of list.getEntries()) {
                  const attribution = entry.attribution[0];
                  results.push(JSON.stringify([
                    entry instanceof PerformanceLongTaskTiming, entry.name, entry.entryType, Number.isInteger(entry.duration), entry.duration >= 50, entry.startTime > 0,
                    entry.attribution.length, attribution instanceof TaskAttributionTiming, attribution.name, attribution.entryType, attribution.containerType,
                    entry.toJSON().attribution[0].containerType, performance.getEntriesByType('longtask').length,
                    attribution.navigationId === entry.navigationId, attribution.id > 0,
                  ]));
                }
                document.title = results.join('|');
              }).observe({ type: 'longtask' });
              window.onload = () => setTimeout(() => { {{LongTaskScript}} });
            </script>
            """);

        Assert.Equal("""[true,"self","longtask",true,true,true,1,true,"unknown","taskattribution","window","window",0,true,true]""", document.Title);
        Assert.Empty(_errors);
    }

    [Fact]
    public void LongTask_ShortTasks_AreNotReported()
    {
        var document = Load("""
            <script>
              document.title = PerformanceObserver.supportedEntryTypes.includes('longtask');
              new PerformanceObserver(list => document.title += list.getEntries().length).observe({ type: 'longtask' });
              window.onload = () => setTimeout(() => {
                const begin = performance.now();
                while (performance.now() < begin + 40);
              });
            </script>
            """);

        Assert.Equal("true", document.Title);
        Assert.Empty(_errors);
    }

    [Fact]
    public void LongTask_BeforeTheObserver_IsBuffered()
    {
        var document = Load($$"""
            <script>
              {{LongTaskScript}}
              setTimeout(() => new PerformanceObserver(list => document.title = list.getEntries().map(entry => entry.name)).observe({ type: 'longtask', buffered: true }));
            </script>
            """);

        Assert.Equal("self", document.Title);
        Assert.Empty(_errors);
    }

    [Fact]
    public void LongTask_InAnIframe_IsAttributedToItsContainer()
    {
        var document = Load($$"""
            <script>
              new PerformanceObserver(list => {
                const entry = list.getEntries()[0];
                const attribution = entry.attribution[0];
                document.title = [entry.name, attribution.containerType, attribution.containerId, attribution.containerName, attribution.containerSrc].join();
              }).observe({ type: 'longtask' });
            </script>
            <iframe id="frame-id" name="frame-name" srcdoc="<script>{{LongTaskScript}}</script>"></iframe>
            """);

        Assert.Equal("same-origin-descendant,iframe,frame-id,frame-name,", document.Title);
        Assert.Empty(_errors);
    }

    [Fact]
    public void LongTask_CanBeObservedByTheHost()
    {
        var document = Load("<p>");

        #region WebPlatform_LongTasks
        var window = document.DefaultView!;
        var longTasks = new List<string>();
        var observer = new PerformanceObserver(window, (entries, _, _) =>
        {
            foreach (var entry in entries.GetEntriesByType("longtask").Cast<PerformanceLongTaskTiming>())
            {
                // e.g. "self: 60 ms" for a script of the window, "same-origin-descendant: 60 ms (iframe #ad)" for an iframe
                var container = entry.Attribution[0];
                longTasks.Add(container.ContainerType == "window" ? $"{entry.Name}: {entry.Duration} ms" : $"{entry.Name}: {entry.Duration} ms ({container.ContainerType} #{container.ContainerId})");
            }
        });
        observer.Observe(new PerformanceObserverInit { Type = "longtask", Buffered = true });
        #endregion

        TestScriptEngines.Evaluate(_engine, window, "setTimeout(() => { " + LongTaskScript + " })");
        _contexts.EventLoop.RunUntilIdle();

        var longTask = Assert.Single(longTasks);
        Assert.StartsWith("self: ", longTask, StringComparison.Ordinal);
        Assert.Empty(_errors);
    }

    [Fact]
    public void LongAnimationFrame_Timer_ReportsTheScript()
    {
        var document = Load($$"""
            <script>
              new PerformanceObserver(list => {
                const entry = list.getEntries()[0];
                const script = entry.scripts[0];
                document.title = JSON.stringify([
                  entry.entryType, entry.name, entry.duration >= 60, entry.renderStart, entry.blockingDuration > 0, entry.scripts.length,
                  script.entryType, script.invokerType, script.invoker, script.sourceFunctionName, script.sourceURL, script.sourceLine > 0, script.windowAttribution, script.window === window,
                ]);
              }).observe({ type: 'long-animation-frame' });
              window.onload = () => setTimeout(function busy() { {{LongTaskScript}} });
            </script>
            """);

        Assert.Equal("""["long-animation-frame","long-animation-frame",true,0,true,1,"script","user-callback","TimerHandler:setTimeout","busy","https://example.com/dir/page.html",true,"self",true]""", document.Title);
        Assert.Empty(_errors);
    }

    [Fact]
    public void LongAnimationFrame_EventListener_IsNamedAfterItsTarget()
    {
        var document = Load($$"""
            <img id="image">
            <script>
              new PerformanceObserver(list => {
                document.title = list.getEntries().flatMap(entry => entry.scripts.map(script => script.invokerType + ' ' + script.invoker)).join();
              }).observe({ type: 'long-animation-frame' });
              const image = document.getElementById('image');
              image.addEventListener('custom', () => { {{LongTaskScript}} });
              window.onload = () => setTimeout(() => image.dispatchEvent(new Event('custom')));
            </script>
            """);

        Assert.Equal("user-callback TimerHandler:setTimeout", document.Title);
        Assert.Empty(_errors);
    }

    [Fact]
    public void LongAnimationFrame_ShortTask_IsNotReported()
    {
        var document = Load("""
            <script>
              window.onload = () => setTimeout(() => {
                const begin = performance.now();
                while (performance.now() < begin + 10);
                setTimeout(() => document.title = performance.getEntriesByType('long-animation-frame').length);
              });
            </script>
            """);

        Assert.Equal("0", document.Title);
        Assert.Empty(_errors);
    }

    [Fact]
    public void LongAnimationFrame_BeforeTheObserver_IsBuffered()
    {
        var document = Load($$"""
            <script>
              {{LongTaskScript}}
              setTimeout(() => new PerformanceObserver(list => document.title = list.getEntries().map(entry => entry.scripts.map(script => script.invokerType))).observe({ type: 'long-animation-frame', buffered: true }));
            </script>
            """);

        Assert.Equal("classic-script", document.Title);
        Assert.Empty(_errors);
    }

    [Fact]
    public void LongAnimationFrame_CanBeObservedByTheHost()
    {
        var document = Load("<p>");

        #region WebPlatform_LongAnimationFrames
        var window = document.DefaultView!;
        var frames = new List<string>();
        var observer = new PerformanceObserver(window, (entries, _, _) =>
        {
            foreach (var entry in entries.GetEntriesByType("long-animation-frame").Cast<PerformanceLongAnimationFrameTiming>())
            {
                // e.g. "60 ms: TimerHandler:setTimeout (busy)", "busy" being the name of the function
                var scripts = entry.Scripts.Select(script => $"{script.Invoker} ({script.SourceFunctionName})");
                frames.Add($"{entry.Duration} ms: {string.Join(", ", scripts)}");
            }
        });
        observer.Observe(new PerformanceObserverInit { Type = "long-animation-frame", Buffered = true });
        #endregion

        TestScriptEngines.Evaluate(_engine, window, "setTimeout(function busy() { " + LongTaskScript + " })");
        _contexts.EventLoop.RunUntilIdle();

        var frame = Assert.Single(frames);
        Assert.EndsWith(" ms: TimerHandler:setTimeout (busy)", frame, StringComparison.Ordinal);
        Assert.Empty(_errors);
    }
}
