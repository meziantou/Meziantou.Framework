---
title: Time and the event loop
description: Run the tasks, the timers and the animation frames of a page, control its clock with a fake time provider, and bound the time it runs.
---

Each top-level browsing context has an event loop ([BrowsingContext.EventLoop](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContext.EventLoop)), shared by its frames: the tasks, the
microtasks, the timers and the animation frames of its documents run on it. Your application runs it when it wants: a
page does nothing between two calls.

## Run the tasks

| Method | Runs |
| --- | --- |
| [RunUntilIdle()](cref:Meziantou.Framework.WebPlatform.Dom.DomEventLoop.RunUntilIdle) | The tasks and the due timers, until nothing is left to run now |
| [RunUntilIdleAsync()](cref:Meziantou.Framework.WebPlatform.Dom.DomEventLoop.RunUntilIdleAsync) | The same, and waits for the operations in progress: the network requests, the documents being loaded |
| [WaitForNextTimerAsync()](cref:Meziantou.Framework.WebPlatform.Dom.DomEventLoop.WaitForNextTimerAsync) | Waits until the next timer or animation frame is due; returns false when there is none |
| [RunTask()](cref:Meziantou.Framework.WebPlatform.Dom.DomEventLoop.RunTask) | One task |

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

A page that keeps a timer (`setInterval`) or an infinite animation never becomes idle: bound the time you run it, with
the `CancellationToken` of these methods or a number of iterations.

## Control the clock

[BrowsingContextOptions.TimeProvider](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContextOptions.TimeProvider) is the clock of the event loop: the timers, the animation frames, the CSS animations
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

The event loop queues the task of a timer once the timer is due, after each task it runs: as in a browser, the callback of
`setTimeout(f, 0)` runs after the tasks that were already queued, and before the tasks that they queue.

## Rendering updates

The animation frames are timers of the event loop, at a fixed rate. Each one runs the steps of a rendering update: the
animations and their events, the `requestAnimationFrame()` callbacks, the styles, the resize and intersection observers.
A screenshot or a geometry API does not wait for them: the layout is computed when it is read. A screenshot also finds the
elements with `content-visibility: auto` that are close to the viewport before it paints, so they are painted with their
contents even before the first animation frame; the `contentvisibilityautostatechange` events are fired by the next tasks.

## Freeze and discard the page

A browser freezes the tabs that are in the background to save resources, and discards them when the memory is low. Set
[IsFrozen](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContext.IsFrozen) to do the same: the documents of the page
and of its frames get the `freeze` event, then their tasks and their timers wait until the page is resumed, which fires
the `resume` event. The tasks are deferred, not lost.

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/JavaScript/HostDeviceApisTests.cs;region=WebPlatform_PageLifecycle;format=md-fenced-code-block;dedent=true -->
```csharp
// Freezes the tab: the freeze event is fired, then the tasks and the timers of the documents wait
document.BrowsingContext!.IsFrozen = true;
```
<!-- endref -->

[Discard()](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContext.Discard) unloads the page and loads it again, as a
browser does when the user goes back to a discarded tab: `document.wasDiscarded` is true in the new document. The pages
are also frozen while they are in the back/forward cache, and the frames whose permissions policy disables
`execution-while-not-rendered` (or `execution-while-out-of-viewport`) are frozen while they are hidden (or out of the
viewport).

## Long tasks

A task that runs scripts for 50 ms or more is a long task ([Long Tasks API](https://w3c.github.io/longtasks/)): it is
reported as a [PerformanceLongTaskTiming](cref:Meziantou.Framework.WebPlatform.Dom.PerformanceLongTaskTiming) to the `longtask` performance observers of the windows of the page. Observe
them from your application to find the scripts that block a page:

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/JavaScript/PerformanceTimelineScriptTests.cs;region=WebPlatform_LongTasks;format=md-fenced-code-block;dedent=true -->
```csharp
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
```
<!-- endref -->

The name of an entry tells where the scripts ran, seen from the window that observes it: `self`, a frame of the same
origin (`same-origin-ancestor`, `same-origin-descendant`, `same-origin`, whose container is in
[Attribution](cref:Meziantou.Framework.WebPlatform.Dom.PerformanceLongTaskTiming.Attribution)), a cross-origin frame, or `multiple-contexts`. With the system clock, a task is timed from its
first script to its end. A fake clock is never read for the long tasks, as each read can advance it: a task lasts from
the first to the last time its scripts read the time (`performance.now()`, the time stamps of the events), so a script
that waits on `performance.now()` for 60 ms makes a long task. The tasks that run no script are not reported, and the
parsing of the document of `Navigate()` counts as one task.

## Long animation frames

A long animation frame ([Long Animation Frames API](https://w3c.github.io/long-animation-frames/)) is a task after which
the rendering is not updated, or the tasks that run until the rendering is updated, when it lasts 50 ms or more. It is
reported as a [PerformanceLongAnimationFrameTiming](cref:Meziantou.Framework.WebPlatform.Dom.PerformanceLongAnimationFrameTiming) to the `long-animation-frame` performance observers of the
window whose scripts ran, and is available from its performance timeline. Its
[Scripts](cref:Meziantou.Framework.WebPlatform.Dom.PerformanceLongAnimationFrameTiming.Scripts) tell which scripts ran 5 ms or more: a script block, an event listener (e.g.
`IMG#logo.onload`), a callback (`FrameRequestCallback`, `TimerHandler:setTimeout`...) or the reactions of a promise of
the platform (`Window.fetch.then`), with the function and the script that define it:

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/JavaScript/PerformanceTimelineScriptTests.cs;region=WebPlatform_LongAnimationFrames;format=md-fenced-code-block;dedent=true -->
```csharp
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
```
<!-- endref -->

A script lasts until the microtasks it queued ran. `renderStart` and `styleAndLayoutStart` are set when the frame ends with
a rendering update: the animation frame callbacks run after `renderStart`, the resize observers after
`styleAndLayoutStart`. The frames are timed like the long tasks: a fake clock is never read for them, the times are the
last times the event loop read the clock (`performance.now()`, the timers). The library does not measure the time it
spends on the styles and the layout: `styleDuration`, `layoutDuration` and the `forced*Duration` of the scripts are 0, and
the frames have no `presentationTime`.

## Run the event loop on your own thread

A host that runs a page on its own thread (as the Playwright server does) sets `EventLoop.WorkSignal`: the callback is
invoked, from any thread, when a task or a timer is queued, so that the thread wakes up and calls [RunTask()](cref:Meziantou.Framework.WebPlatform.Dom.DomEventLoop.RunTask) and
[PromoteDueTimers()](cref:Meziantou.Framework.WebPlatform.Dom.DomEventLoop.PromoteDueTimers). The callback must not run the event loop itself.
