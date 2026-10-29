---
title: Time and the event loop
description: Run the tasks, the timers and the animation frames of a page, control its clock with a fake time provider, and bound the time it runs.
---

Each top-level browsing context has an event loop (`BrowsingContext.EventLoop`), shared by its frames: the tasks, the
microtasks, the timers and the animation frames of its documents run on it. Your application runs it when it wants: a
page does nothing between two calls.

## Run the tasks

| Method | Runs |
| --- | --- |
| `RunUntilIdle()` | The tasks and the due timers, until nothing is left to run now |
| `RunUntilIdleAsync()` | The same, and waits for the operations in progress: the network requests, the documents being loaded |
| `WaitForNextTimerAsync()` | Waits until the next timer or animation frame is due; returns false when there is none |
| `RunTask()` | One task |

To run a page until it has nothing left to do, timers included:

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

A page that keeps a timer (`setInterval`) or an infinite animation never becomes idle: bound the time you run it, with
the `CancellationToken` of these methods or a number of iterations.

## Control the clock

`BrowsingContextOptions.TimeProvider` is the clock of the event loop: the timers, the animation frames, the CSS animations
and transitions and `performance.now()` follow it. The clock of `Date` is the `TimeProvider` of the
[script engine options](../06-Scripting/02-RunningJavaScript.md). With a fake clock, such as the `FakeTimeProvider` of
Microsoft.Extensions.TimeProvider.Testing, tests run without waiting and give the same results every time:

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/Dom/TimerTests.cs;region=WebPlatform_FakeClock;format=md-fenced-code-block;dedent=true -->
```csharp
var clock = new FakeTimeProvider(); // Microsoft.Extensions.TimeProvider.Testing
using var engine = new JavaScriptScriptEngine();
using var context = new BrowsingContext(new BrowsingContextOptions { ScriptEngine = engine, TimeProvider = clock });

var document = await context.NavigateAsync("https://example.com/", _ => """
    <p id="ticks">0</p>
    <script>setInterval(() => document.getElementById('ticks').textContent++, 1000);</script>
    """);

// The timers follow the clock of the event loop: advance it instead of waiting
for (var i = 0; i < 3; i++)
{
    clock.Advance(TimeSpan.FromSeconds(1));
    context.EventLoop.RunUntilIdle(); // runs the callbacks of the timers that are due
}
Assert.Equal("3", document.GetElementById("ticks")!.TextContent);
```
<!-- endref -->

Advancing the clock queues the timers that are due; run the event loop to run their callbacks. Advance the clock step by
step when a callback starts another timer.

## Rendering updates

The animation frames are timers of the event loop, at a fixed rate. Each one runs the steps of a rendering update: the
animations and their events, the `requestAnimationFrame()` callbacks, the styles, the resize and intersection observers.
A screenshot or a geometry API does not wait for them: the layout is computed when it is read.

## Run the event loop on your own thread

A host that runs a page on its own thread (as the Playwright server does) sets `EventLoop.WorkSignal`: the callback is
invoked, from any thread, when a task or a timer is queued, so that the thread wakes up and calls `RunTask()` and
`PromoteDueTimers()`. The callback must not run the event loop itself.
