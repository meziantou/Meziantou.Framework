---
title: Scripting
description: Run the scripts of the pages with a JavaScript engine written in .NET, exchange values with them, bound their resources, and run WebAssembly.
---

The library never runs scripts by itself. Give a script engine to a browsing context, and the pages run their scripts as
in a browser: the `script` elements, the modules, the event handlers, the timers, the `javascript:` URLs, the workers.
The `Meziantou.Framework.WebPlatform.JavaScript` package provides the engine, based on Meziantou.Framework.JavaScriptEngine,
an ECMAScript engine written in .NET.

<InstallNuGetPackage Id="Meziantou.Framework.WebPlatform.JavaScript" />

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

## Without a script engine

Without an engine, the scripts are never fetched or run, the event handler attributes (`onclick="..."`) have no effect,
and the `javascript:` URLs are ignored. The DOM, the styles and the layout work the same: a page without scripts is often
all you need to read server-rendered HTML.

## What the scripts can use

The scripts see the DOM and the web APIs implemented by the library, through the bindings of the browsers: the DOM and
the events, `fetch()` and `XMLHttpRequest` (with a document loader), the timers, the storage, the module scripts and the
import maps, the workers, the custom elements, the canvases (with a rendering backend), Web Crypto, the streams, and
WebAssembly (with a WebAssembly runtime). The scripts have no access to .NET: they only see what a page sees in a
browser, and the functions that you add.

## In this section

- [Running JavaScript](02-RunningJavaScript.md): the engine, its options, the console and the errors of the scripts.
- [Calling JavaScript from .NET](03-CallingJavaScriptFromDotNet.md): evaluate code, call functions, convert values, and
  add .NET functions to the pages.
- [Running untrusted pages](04-RunningUntrustedPages.md): the limits of the engine and the requests of the pages.
- [WebAssembly](05-WebAssembly.md): run the WebAssembly modules of the pages with Wasmtime.
