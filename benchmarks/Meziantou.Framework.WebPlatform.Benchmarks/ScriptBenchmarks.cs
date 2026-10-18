using BenchmarkDotNet.Attributes;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Jint;

namespace Meziantou.Framework.WebPlatform.Benchmarks;

/// <summary>Parses a script-heavy page with the Jint script engine: realm creation, DOM-heavy scripts and event dispatch.</summary>
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
        using var engine = new JintScriptEngine();
        using var context = new BrowsingContext(new BrowsingContextOptions { ScriptEngine = engine });
        var document = context.Navigate("https://example.com/", _ => html);
        context.EventLoop.RunUntilIdle();
        return document.Title;
    }
}
