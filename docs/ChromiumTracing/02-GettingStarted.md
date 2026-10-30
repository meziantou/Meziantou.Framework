---
title: Getting started
description: Install the package, write events to a trace file, and open the file in a trace viewer.
---

## Install the package

Add the `Meziantou.Framework.ChromiumTracing` package to a project:

```bash
dotnet add package Meziantou.Framework.ChromiumTracing
```

The examples of this documentation need `using Meziantou.Framework.ChromiumTracing;`.

## Write a trace file

Create a writer for a file, write events, and dispose the writer:

<!-- ref:../../tests/Meziantou.Framework.ChromiumTracing.Tests/WriterTests.cs;region=ChromiumTracing_BasicExample;format=md-fenced-code-block;dedent=true -->
```csharp
// Create a writer
await using var writer = ChromiumTracingWriter.Create("trace.json");

// Write a complete event (shows as a box in the timeline)
await writer.WriteEventAsync(new ChromiumTracingCompleteEvent
{
    Name = "My Operation",
    Category = "category1",
    Timestamp = DateTimeOffset.UtcNow,
    Duration = TimeSpan.FromMilliseconds(150),
    ProcessId = Environment.ProcessId,
    ThreadId = Environment.CurrentManagedThreadId,
    Arguments = new Dictionary<string, object?>
    {
        ["result"] = "success",
        ["count"] = 42,
    },
});
```
<!-- endref -->

- `ChromiumTracingWriter.Create(path)` creates the file, or overwrites it.
- [WriteEventAsync](cref:Meziantou.Framework.ChromiumTracing.ChromiumTracingWriter.WriteEventAsync) writes one event. A complete event, as here, is an operation with a start time and a duration: the
  viewer shows it as a box on the row of its process and thread.
- [ProcessId](cref:Meziantou.Framework.ChromiumTracing.ChromiumTracingEvent.ProcessId) and [ThreadId](cref:Meziantou.Framework.ChromiumTracing.ChromiumTracingEvent.ThreadId) choose the row. They do not have to be real process and thread identifiers: any numbers that
  group the events the way you want to see them work.
- The writer is `IAsyncDisposable` only: declare it with `await using`. Disposing it closes the JSON array and the file.

## Measure an operation

To trace an operation of your program, take the start time, measure the duration with a `Stopwatch`, and write a
complete event when the operation ends:

<!-- ref:../../tests/Meziantou.Framework.ChromiumTracing.Tests/WriterTests.cs;region=ChromiumTracing_MeasureOperation;format=md-fenced-code-block;dedent=true -->
```csharp
using var stream = new MemoryStream();
await using (var writer = new ChromiumTracingWriter(stream))
{
    var start = DateTimeOffset.UtcNow;
    var stopwatch = Stopwatch.StartNew();

    // ... the operation to measure ...

    await writer.WriteEventAsync(new ChromiumTracingCompleteEvent
    {
        Name = "Compile",
        Category = "build",
        Timestamp = start,
        Duration = stopwatch.Elapsed,
        ProcessId = Environment.ProcessId,
        ThreadId = Environment.CurrentManagedThreadId,
    });
}

using var trace = JsonDocument.Parse(stream.ToArray());
Assert.Equal(["X"], trace.RootElement.EnumerateArray().Select(item => item.GetProperty("ph").GetString()).ToArray());
```
<!-- endref -->

The next examples of this documentation write to a `MemoryStream` and check the JSON they produce. Replace the stream with
`ChromiumTracingWriter.Create("trace.json")` to get a file. See [Files and streams](05-FilesAndStreams.md).

## View the trace

1. Open `chrome://tracing` in Chrome, or `edge://tracing` in Edge.
1. Click **Load** and select the trace file.
1. Zoom and pan the timeline, and select an event to see its properties and arguments.

Other viewers read the same format, such as [Perfetto UI](https://ui.perfetto.dev).

## Next steps

- [Event types](03-EventTypes.md): nested durations, counters, asynchronous operations and more.
- [Event properties](04-EventProperties.md): categories, arguments and colors.
