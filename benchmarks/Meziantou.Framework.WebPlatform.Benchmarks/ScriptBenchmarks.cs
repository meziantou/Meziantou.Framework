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
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        for (var i = 0; i < count; i++)
        {
            operation();
        }

        var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(start);
        Console.WriteLine($"{scenario}: {elapsed.TotalMilliseconds * 1000 / count:F1} us, {(GC.GetAllocatedBytesForCurrentThread() - allocated) / count / 1024.0:F1} KB per operation");
        return 0;
    }
}
