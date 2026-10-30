---
title: Capturing activities
description: Capture the System.Diagnostics activities started by a piece of code, for instance to check the traces of a library in a test.
---

`ScopedActivityListener` (in the [Meziantou.Framework.Diagnostics](cref:Meziantou.Framework.Diagnostics) namespace) captures the `Activity` instances started by
the current code, and only by it. Use it in tests to check that code is traced as expected, even when other tests run in
parallel.

## Capture the activities of a block of code

<!-- ref:../../tests/Meziantou.Framework.Tests/Diagnostics/ScopedActivityListenerTests.cs;region=ScopedActivityListener_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
using var source = new ActivitySource("Sample.ScopedActivityListener");
using var listener = new ScopedActivityListener(new() { SourceNames = ["Sample.ScopedActivityListener"] });

// Code that starts activities, such as a call to an instrumented library
using (source.StartActivity("DoWork"))
{
    await Task.Delay(10);
}

var activity = Assert.Single(listener.Activities);
Assert.Equal("DoWork", activity.OperationName);
Assert.True(activity.Duration > TimeSpan.Zero);
```
<!-- endref -->

- The listener captures the activities started by the asynchronous flow that created it, from its creation to its
  disposal: the code that follows it, including the tasks that code starts. Activities started by other
  threads or other tests are ignored.
- `Activities` lists the captured activities once they are stopped, in the order they stopped. An activity that is still
  running when the listener is disposed is never reported.
- The activities are live objects: their tags, status and duration are final once they are stopped.
- Dispose the listener to stop listening.

## Options

Pass a `ScopedActivityListenerOptions` to the constructor:

| Property | Default | Effect |
| --- | --- | --- |
| `SourceNames` | All sources | The names of the `ActivitySource` to listen to, compared ordinally. Use an empty string for the activities created with `new Activity(name)` |
| `ShouldListenTo` | All sources | A predicate that chooses the sources. It replaces `SourceNames` when both are set |
| `SamplingResult` | `AllData` | The sampling decision returned for the activities of the scope |
| `CaptureChildActivities` | `true` | Also captures the activities whose parent is captured, even when they run outside the asynchronous flow |
| `MaxActivityCount` | `int.MaxValue` | The number of stopped activities kept in `Activities`. When the limit is reached, the oldest are dropped |

- `ShouldListenTo` is evaluated once per source and its result is cached by the runtime: it must only depend on the
  source, not on the current code.
- `AllDataAndRecorded` sets the `Recorded` flag of the activities, which is sent to other services in the `traceparent`
  header and changes their own sampling decisions.

While the listener exists, every matching source has a listener for the whole process. Libraries that check
`ActivitySource.HasListeners`, such as `HttpClient` or ASP.NET Core, switch to their instrumented code: `HttpClient`, for
instance, sends the `traceparent` header for the requests made in the scope. Set `SourceNames` to listen to the sources
you need only.

## React when an activity starts or stops

<!-- ref:../../tests/Meziantou.Framework.Tests/Diagnostics/ScopedActivityListenerTests.cs;region=ScopedActivityListener_Events;format=md-fenced-code-block;dedent=true -->
```csharp
using var source = new ActivitySource("Sample.ScopedActivityListener.Events");
using var listener = new ScopedActivityListener(new() { SourceNames = ["Sample.ScopedActivityListener.Events"] });

var started = new List<string>();
var stopped = new List<string>();
listener.ActivityStarted += (_, e) => started.Add(e.Activity.OperationName);
listener.ActivityStopped += (_, e) => stopped.Add(e.Activity.OperationName);

using (source.StartActivity("Parent"))
{
    source.StartActivity("Child")?.Dispose();
}

Assert.Equal(["Parent", "Child"], started);
Assert.Equal(["Child", "Parent"], stopped);

// Activities lists the stopped activities, in the order they stopped
Assert.Equal(["Child", "Parent"], listener.Activities.Select(activity => activity.OperationName));
```
<!-- endref -->

- `ActivityStarted` and `ActivityStopped` are raised synchronously, on the thread that starts or stops the activity.
  `ActivityEventArgs.Activity` is the activity. In `ActivityStarted`, its tags, status and duration are not set yet.
- An exception thrown by a handler is ignored, so a failing handler cannot break the code being observed.

## Stream the activities

`GetActivitiesAsync` returns the activities already captured, then each activity as it stops, until the listener is
disposed or the cancellation token is canceled:

<!-- ref:../../tests/Meziantou.Framework.Tests/Diagnostics/ScopedActivityListenerTests.cs;region=ScopedActivityListener_GetActivitiesAsync;format=md-fenced-code-block;dedent=true -->
```csharp
using var source = new ActivitySource("Sample.ScopedActivityListener.Stream");
using var listener = new ScopedActivityListener(new() { SourceNames = ["Sample.ScopedActivityListener.Stream"] });

source.StartActivity("First")?.Dispose();
source.StartActivity("Second")?.Dispose();

// The enumeration replays the captured activities, then waits for new ones until the listener is disposed
listener.Dispose();

var names = new List<string>();
await foreach (var activity in listener.GetActivitiesAsync())
{
    names.Add(activity.OperationName);
}

Assert.Equal(["First", "Second"], names);
```
<!-- endref -->

Several enumerations can run at the same time: each of them receives every activity.
