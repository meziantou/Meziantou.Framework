---
title: Running untrusted pages
description: Bound the time and the memory of the scripts, keep the event loop under control, and filter the requests of untrusted pages.
---

The scripts run in your process. A page you do not control can loop forever, allocate memory, keep the event loop busy,
or make your server send requests. The engine and the browsing context give you the means to bound each of these.

## Bound the scripts

`JavaScriptScriptEngineOptions` limits each entry into JavaScript (a script, an event listener, a timer callback):

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/JavaScript/JavaScriptScriptEngineTests.cs;region=WebPlatformJavaScript_Limits;format=md-fenced-code-block;dedent=true -->
```csharp
var errors = new List<ScriptError>();
using var engine = new JavaScriptScriptEngine(new JavaScriptScriptEngineOptions
{
    Timeout = TimeSpan.FromSeconds(2),   // per script, event listener or timer callback
    MaxStatements = 100_000,             // loop iterations and calls
    MemoryLimit = 64 * 1024 * 1024,      // bytes allocated
});

using var context = new BrowsingContext(new BrowsingContextOptions
{
    ScriptEngine = engine,
    UnhandledScriptError = (_, error) => errors.Add(error),
});

var document = await context.NavigateAsync("https://example.com/", _ => """
    <script>while (true) { }</script>
    <p>The page goes on after the script is stopped</p>
    """);
await context.EventLoop.RunUntilIdleAsync();

Assert.Single(errors);
Assert.NotNull(document.QuerySelector("p"));
```
<!-- endref -->

- `Timeout` (10 seconds by default), `MemoryLimit` (256 MB allocated by default), `RecursionLimit` (512 calls by
  default), `RegexTimeout` (2 seconds by default) and `MaxStatements` (no limit by default) stop the script that exceeds
  them, and report the error to `UnhandledScriptError`. The page goes on.
- The engine has no access to .NET: the scripts only see the DOM, the web APIs and the JavaScript built-ins, and the
  functions you add in `OnRealmCreated`.

## Bound the page

The limits apply to each entry into JavaScript, not to the page: a page can start a timer every millisecond
(`setInterval(f, 0)`) and keep the event loop busy forever. Bound the time you run it: pass a `CancellationToken` to
`RunUntilIdleAsync()` and `WaitForNextTimerAsync()`, and stop waiting for the timers after a while. Then dispose the
browsing context.

## Filter the requests

The scripts, the frames and the navigations request URLs through the `DocumentLoader`. `DocumentLoader.Create()` requests
any URL, including the services of your private network: give untrusted pages a loader that filters the destinations
(see [Network requests](../05-LoadingPages/02-NetworkRequests.md#protect-your-network-from-untrusted-pages)), or no
loader at all.

## The other protections of the browsers

The library implements the security model of the browsers between the pages: the same-origin policy, CORS, the content
security policies (`Content-Security-Policy`), the sandboxed frames, mixed content and Trusted Types. They protect the
pages from each other; they do not protect your process from a page.
