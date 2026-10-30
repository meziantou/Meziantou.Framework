---
title: Running JavaScript
description: Plug the JavaScript engine into a browsing context, set its options, and receive the console messages and the errors of the scripts.
---

Create a `JavaScriptScriptEngine` and give it to the browsing context. Navigate with `NavigateAsync`, which waits for the
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
    UnhandledScriptError = (window, error) => Console.Error.WriteLine(error),
    ConsoleMessage = (window, message) => Console.WriteLine(message.Text), // console.log(), console.count()...
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
- The external scripts and the modules are fetched by the `DocumentLoader`: without a loader, only the inline scripts run.
- The engine is disposable. Several browsing contexts can share it: each window is a realm of its own, with its own
  global object.
- A worker gets its own engine, with the same options, on its own thread.

## Options

| `JavaScriptScriptEngineOptions` | Default | Effect |
| --- | --- | --- |
| `Timeout` | 10 seconds | The longest time an entry into JavaScript runs: a script, an event listener, a timer callback |
| `MemoryLimit` | 256 MB | The memory an entry into JavaScript allocates |
| `RecursionLimit` | 512 calls | The depth of the calls; a deeper call throws a `RangeError` |
| `MaxStatements` | none | The number of loop iterations and calls of an entry |
| `RegexTimeout` | 2 seconds | The longest time of a regular expression match |
| `TimeProvider` | the system clock | The clock of `Date` and the local time zone |
| `ConfigureEngine` | none | Sets the other options of the engine, such as the time zone and the default locale of `Intl` |

When a limit is exceeded, the script stops and its error is reported: see
[Running untrusted pages](04-RunningUntrustedPages.md).

## Console messages and errors

`BrowsingContextOptions.ConsoleMessage` receives the messages of `console.log()`, `console.error()` and the other
methods of the console, formatted as the console of a browser formats them (`ConsoleMessage.Text`), with their level and
their arguments. `BrowsingContextOptions.UnhandledScriptError` receives the errors that no script handled: the exceptions
of the scripts and the rejected promises, with their message, their file and their position (`ScriptError`). The errors do
not stop the page: the next scripts run, as in a browser.

## Modules, workers and worklets

`<script type="module">`, `import()`, the import maps, the JSON and CSS modules, the dedicated and shared workers and the
worklets are supported. Each worker runs its own event loop on its own thread; the event loop of the page waits for its
workers in `RunUntilIdleAsync()`.
