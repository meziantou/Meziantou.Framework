---
title: Running JavaScript
description: Plug the JavaScript engine into a browsing context, set its options, and receive the console messages and the errors of the scripts.
---

Create a [JavaScriptScriptEngine](cref:Meziantou.Framework.WebPlatform.JavaScript.JavaScriptScriptEngine) and give it to the browsing context. Navigate with [NavigateAsync](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContext.NavigateAsync), which waits for the
scripts that block the parser, then run the event loop:

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/JavaScript/JavaScriptScriptEngineTests.cs;region=WebPlatformJavaScript_ExecuteScripts;format=md-fenced-code-block;dedent=true -->
```csharp
using var engine = new JavaScriptScriptEngine(new JavaScriptScriptEngineOptions
{
    Timeout = TimeSpan.FromSeconds(5), // per script or callback
});

using var context = new BrowsingContext(new BrowsingContextOptions
{
    ScriptEngine = engine,
    DocumentLoader = DocumentLoader.Create(), // optional: external scripts, modules, fetch, XMLHttpRequest, WebSocket, EventSource...
    UnhandledScriptError = (_, error) => Console.Error.WriteLine(error),
    ConsoleMessage = (_, message) => Console.WriteLine(message.Text), // console.log(), console.count()...
});

var document = await context.NavigateAsync("https://example.com/", _ => """
    <title>Example</title>
    <ul id=list></ul>
    <script>
      setTimeout(() => document.getElementById('list').innerHTML = '<li>a<li>b', 100);
    </script>
    """);

// Run the tasks and the timers until there is nothing left
do { await context.EventLoop.RunUntilIdleAsync(); } while (await context.EventLoop.WaitForNextTimerAsync());

Assert.Equal("<li>a</li><li>b</li>", document.GetElementById("list")!.InnerHtml);

// Evaluate code in the realm of a window
var title = engine.Evaluate(context.Window, "document.title");
Assert.Equal("Example", title.AsString());
```
<!-- endref -->

- The scripts run on the thread that runs the event loop, when the event loop runs their tasks: the inline scripts while
  the document is parsed, the timers and the event listeners when their tasks run.
- The external scripts and the modules are fetched by the [DocumentLoader](cref:Meziantou.Framework.WebPlatform.Dom.DocumentLoader): without a loader, only the inline scripts run.
- The engine is disposable. Several browsing contexts can share it: each window is a realm of its own, with its own
  global object.
- A worker gets its own engine, with the same options, on its own thread.

## Options

| [JavaScriptScriptEngineOptions](cref:Meziantou.Framework.WebPlatform.JavaScript.JavaScriptScriptEngineOptions) | Default | Effect |
| --- | --- | --- |
| [Timeout](cref:Meziantou.Framework.WebPlatform.JavaScript.JavaScriptScriptEngineOptions.Timeout) | 10 seconds | The longest time an entry into JavaScript runs: a script, an event listener, a timer callback |
| [MemoryLimit](cref:Meziantou.Framework.WebPlatform.JavaScript.JavaScriptScriptEngineOptions.MemoryLimit) | 256 MB | The memory an entry into JavaScript allocates |
| [RecursionLimit](cref:Meziantou.Framework.WebPlatform.JavaScript.JavaScriptScriptEngineOptions.RecursionLimit) | 512 calls | The depth of the calls; a deeper call throws a `RangeError` |
| [MaxStatements](cref:Meziantou.Framework.WebPlatform.JavaScript.JavaScriptScriptEngineOptions.MaxStatements) | none | The number of loop iterations and calls of an entry |
| [RegexTimeout](cref:Meziantou.Framework.WebPlatform.JavaScript.JavaScriptScriptEngineOptions.RegexTimeout) | 2 seconds | The longest time of a regular expression match |
| [TimeProvider](cref:Meziantou.Framework.WebPlatform.JavaScript.JavaScriptScriptEngineOptions.TimeProvider) | the system clock | The clock of `Date` and the local time zone |
| [ConfigureEngine](cref:Meziantou.Framework.WebPlatform.JavaScript.JavaScriptScriptEngineOptions.ConfigureEngine) | none | Sets the other options of the engine, such as the time zone and the default locale of `Intl` |
| [ScriptCache](cref:Meziantou.Framework.WebPlatform.JavaScript.JavaScriptScriptEngineOptions.ScriptCache) | the cache of the process | The parsed scripts reused by the documents: see [Load the same pages many times](#load-the-same-pages-many-times) |

When a limit is exceeded, the script stops and its error is reported: see
[Running untrusted pages](04-RunningUntrustedPages.md).

## Load the same pages many times

The engines keep the classic scripts they parse in a cache: the inline scripts, the external scripts and the event
handler attributes such as `onclick`. When a document runs a script that an engine already parsed, with the same source
text, at the same URL and the same position in its document, it reuses it: the script is not parsed again, and the
functions that ran often enough to be compiled to .NET code run compiled from the start. A page loaded again and again,
or a script shared by the pages of a site, runs much faster after the first load, even with a new engine for each page.

By default, all the engines of the process share
[JavaScriptScriptCache.Shared](cref:Meziantou.Framework.WebPlatform.JavaScript.JavaScriptScriptCache.Shared). Give
engines a cache of their own to set its capacity, or set
[ScriptCache](cref:Meziantou.Framework.WebPlatform.JavaScript.JavaScriptScriptEngineOptions.ScriptCache) to `null` to
parse each script for each document:

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/JavaScript/JavaScriptScriptEngineTests.cs;region=WebPlatformJavaScript_ScriptCache;format=md-fenced-code-block;dedent=true -->
```csharp
// The engines that share a cache parse each script once: the default cache is JavaScriptScriptCache.Shared
var cache = new JavaScriptScriptCache(capacity: 1_000_000); // characters of source text
for (var i = 0; i < 3; i++)
{
    using var engine = new JavaScriptScriptEngine(new JavaScriptScriptEngineOptions { ScriptCache = cache });
    using var context = new BrowsingContext(new BrowsingContextOptions { ScriptEngine = engine });
    var document = context.Navigate("https://example.com/", _ => "<script>document.title = 'loaded'</script>");
    context.EventLoop.RunUntilIdle();
    Assert.Equal("loaded", document.Title);
}

Assert.Equal(1, cache.Count);
```
<!-- endref -->

- The capacity is the total length of the source texts and the URLs of the cached scripts, in characters:
  [DefaultCapacity](cref:Meziantou.Framework.WebPlatform.JavaScript.JavaScriptScriptCache.DefaultCapacity), 8 million
  by default. The least recently used scripts are removed first, and a script longer than the capacity is not cached.
  The memory of a script grows with the functions compiled to .NET code, so lower the capacity when the process loads
  many different pages and memory matters more than speed.
- A cached script behaves as a script parsed again: the errors and the stack traces have the positions in its document,
  and a script that runs again in the same window, such as the same source text inserted twice by the DOM, is parsed
  again, so its tagged templates are new objects.
- The cache is thread-safe: the engines of the workers and of other threads share it.
  [Clear](cref:Meziantou.Framework.WebPlatform.JavaScript.JavaScriptScriptCache.Clear) empties it.
- The modules are not cached.

## Console messages and errors

[BrowsingContextOptions.ConsoleMessage](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContextOptions.ConsoleMessage) receives the messages of `console.log()`, `console.error()` and the other
methods of the console, formatted as the console of a browser formats them ([ConsoleMessage.Text](cref:Meziantou.Framework.WebPlatform.Scripting.ConsoleMessage.Text)), with their level and
their arguments. [BrowsingContextOptions.UnhandledScriptError](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContextOptions.UnhandledScriptError) receives the errors that no script handled: the exceptions
of the scripts and the rejected promises, with their message, their file and their position ([ScriptError](cref:Meziantou.Framework.WebPlatform.Scripting.ScriptError)). The errors do
not stop the page: the next scripts run, as in a browser.

## Modules, workers and worklets

`<script type="module">`, `import()`, the import maps, the JSON and CSS modules, the dedicated and shared workers and the
worklets are supported. Each worker runs its own event loop on its own thread; the event loop of the page waits for its
workers in [RunUntilIdleAsync()](cref:Meziantou.Framework.WebPlatform.Dom.DomEventLoop.RunUntilIdleAsync).

The SVG `script` elements (in an SVG document or inline in HTML) run as the HTML ones: the classic scripts, the module
scripts and the import maps, with the `async` and `defer` attributes. An external classic script without `async` or
`defer` pauses the parser until it runs, and a module script runs when the document has finished parsing. Their source is
their `href` attribute.

The messages posted between the windows, the workers and the worklets (`postMessage()`) are cloned as in a browser. The
transfer list moves the `ArrayBuffer` objects, the message ports and the streams: a `ReadableStream`, a `WritableStream`
or a `TransformStream` posted with `[stream]` is received as a stream of the other side, whose chunks, backpressure,
cancellation and errors go through a message port. The chunks are cloned when they are read, so a chunk that cannot be
cloned errors both sides of the stream.
