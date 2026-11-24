using BenchmarkDotNet.Attributes;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.JavaScript;

namespace Meziantou.Framework.WebPlatform.Benchmarks;

/// <summary>Parses a script-heavy page with the JavaScript script engine: realm creation, DOM-heavy scripts and event dispatch.</summary>
[MemoryDiagnoser]
public class ScriptBenchmarks
{
    private const string Html = """
        <!DOCTYPE html>
        <ul id=list></ul>
        <button id=button onclick="window.clicks++">click</button>
        <script>
          window.clicks = 0;
          const list = document.getElementById('list');
          for (let i = 0; i < 500; i++) {
            const item = document.createElement('li');
            item.className = 'item item-' + i;
            item.setAttribute('data-index', String(i));
            item.textContent = 'Item ' + i;
            list.appendChild(item);
          }

          let total = 0;
          for (const item of document.querySelectorAll('li.item')) {
            total += Number(item.dataset.index);
          }

          const button = document.getElementById('button');
          button.addEventListener('click', e => { e.target.dataset.clicked = 'true'; });
          for (let i = 0; i < 200; i++) {
            button.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
          }

          document.title = String(total) + ' ' + window.clicks;
        </script>
        """;

    [Benchmark]
    public string ParseAndRun() => Run(Html);

    /// <summary>A realm and its global object: most interface objects are created on first use.</summary>
    [Benchmark]
    public string CreateRealm() => Run("<script>document.title = typeof HTMLElement</script>");

    /// <summary>Events dispatched by the DOM to JavaScript listeners (the conversions of the callbacks and of the events).</summary>
    [Benchmark]
    public string DispatchEvents() => Run("""
        <div id=target></div>
        <script>
          const target = document.getElementById('target');
          let count = 0;
          target.addEventListener('custom', e => { count += e.detail; });
          for (let i = 0; i < 1000; i++) {
            target.dispatchEvent(new CustomEvent('custom', { detail: 1 }));
          }

          document.title = String(count);
        </script>
        """);

    private static string Run(string html)
    {
        // Each operation has its own browsing context, and its own event loop
        using var engine = new JavaScriptScriptEngine();
        using var context = new BrowsingContext(new BrowsingContextOptions { ScriptEngine = engine });
        var document = context.Navigate("https://example.com/", _ => html);
        context.EventLoop.RunUntilIdle();
        return document.Title;
    }
}

/// <summary>
/// The named properties of element.dataset (a legacy platform object), read in a loop as the encoding tests of WPT do. The
/// page is loaded once: each operation runs the same listener, so it measures the reads, not the compilation of the script.
/// </summary>
[MemoryDiagnoser]
public class DatasetBenchmarks
{
    private JavaScriptScriptEngine? _engine;
    private BrowsingContext? _context;
    private Element? _target;

    [GlobalSetup]
    public void Setup()
    {
        _engine = new JavaScriptScriptEngine();
        _context = new BrowsingContext(new BrowsingContextOptions { ScriptEngine = _engine });
        var document = _context.Navigate("https://example.com/", _ => """
            <div id=target></div>
            <script>
              const target = document.getElementById('target');
              for (let i = 0; i < 200; i++) {
                const span = document.createElement('span');
                span.setAttribute('data-cp', String(i));
                span.setAttribute('data-bytes', 'A' + i);
                target.appendChild(span);
              }

              const spans = document.querySelectorAll('span');
              target.addEventListener('read', () => {
                let n = 0;
                for (let i = 0; i < spans.length; i++) {
                  n += spans[i].dataset.cp.length + spans[i].dataset.bytes.length;
                }

                document.title = String(n);
              });
            </script>
            """);
        _context.EventLoop.RunUntilIdle();
        _target = document.GetElementById("target");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _context?.Dispose();
        _engine?.Dispose();
    }

    [Benchmark]
    public bool ReadNamedProperties() => _target!.DispatchEvent(new Event("read"));
}

/// <summary>Runs a scenario of <see cref="ScriptBenchmarks"/> in a loop, to profile it (time and allocations per operation).</summary>
internal static class ScriptProfile
{
    public static int Run(string scenario, int count)
    {
        var benchmarks = new ScriptBenchmarks();
        Func<string> operation = scenario switch
        {
            "ParseAndRun" => benchmarks.ParseAndRun,
            "CreateRealm" => benchmarks.CreateRealm,
            "DispatchEvents" => benchmarks.DispatchEvents,
            _ => throw new ArgumentException("Unknown scenario: " + scenario, nameof(scenario)),
        };

        for (var i = 0; i < Math.Min(count, 20); i++)
        {
            operation();
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var collections = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
        var pauses = GC.GetTotalPauseDuration();
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        for (var i = 0; i < count; i++)
        {
            operation();
        }

        var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(start);
        Console.WriteLine($"{scenario}: {elapsed.TotalMilliseconds * 1000 / count:F1} us, {(GC.GetAllocatedBytesForCurrentThread() - allocated) / count / 1024.0:F1} KB per operation, " +
            $"GC gen0 {GC.CollectionCount(0) - collections.Item1} gen1 {GC.CollectionCount(1) - collections.Item2} gen2 {GC.CollectionCount(2) - collections.Item3}, " +
            $"pauses {(GC.GetTotalPauseDuration() - pauses).TotalMilliseconds * 1000 / count:F1} us per operation");
        return 0;
    }
}
