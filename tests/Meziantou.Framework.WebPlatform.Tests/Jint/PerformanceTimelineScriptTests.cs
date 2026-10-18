using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Jint;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Tests.Scripting;

namespace Meziantou.Framework.WebPlatform.Tests.Jint;

/// <summary>The performance timeline used by the scripts: User Timing, Navigation Timing and the performance observers.</summary>
public sealed class PerformanceTimelineScriptTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly List<ScriptError> _errors = [];
    private readonly RecordingLoader _loader = new();
    private readonly JintScriptEngine _engine = new();

    public void Dispose()
    {
        _contexts.Dispose();
        _engine.Dispose();
    }

    private Document Load(string html)
    {
        var context = _contexts.Create(new BrowsingContextOptions
        {
            ScriptEngine = _engine,
            DocumentLoader = _loader,
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
}
