---
title: Event types
description: Durations, complete events, instants, counters, asynchronous operations, flows, objects and metadata, and the phase of each one.
---

Each event type is a class that derives from `ChromiumTracingEvent`. Its `Type` property returns the phase of the event
in the format, the `ph` field of the JSON, which tells the viewer how to show it.

| Class | Phase | Shows |
| --- | --- | --- |
| `ChromiumTracingDurationBeginEvent`, `ChromiumTracingDurationEndEvent` | `B`, `E` | The start and the end of an operation, as a box |
| `ChromiumTracingCompleteEvent` | `X` | An operation with a start and a duration, as a box |
| `ChromiumTracingInstantEvent` | `i` | A point in time |
| `ChromiumTracingCounterEvent` | `C` | Values over time, as a chart |
| `ChromiumTracingAsyncBeginEvent`, `ChromiumTracingAsyncInstantEvent`, `ChromiumTracingAsyncEndEvent` | `b`, `n`, `e` | An asynchronous operation, which can overlap others |
| `ChromiumTracingFlowBeginEvent`, `ChromiumTracingFlowStepEvent`, `ChromiumTracingFlowEndEvent` | `s`, `t`, `f` | An arrow between events |
| `ChromiumTracingObjectCreatedEvent`, `ChromiumTracingObjectSnapshotEvent`, `ChromiumTracingObjectDestroyedEvent` | `N`, `O`, `D` | The lifetime and the state of an object |
| `ChromiumTracingMetadataEvent` | `M` | The names and the order of the processes and threads |
| `ChromiumTracingMemoryDumpGlobalEvent`, `ChromiumTracingMemoryDumpProcessEvent` | `V`, `v` | A memory dump, of the system or of a process |
| `ChromiumTracingMarkEvent` | `R` | A mark |
| `ChromiumTracingClockSyncEvent` | `c` | A clock synchronization point; its name is always `clock_sync` |
| `ChromiumTracingContextBeginEvent`, `ChromiumTracingContextEndEvent` | `(`, `)` | The start and the end of a context |
| `ChromiumTracingLinkIdEvent` | `=` | A link between two ids |

The specification of the format describes each phase in detail. The examples below show the most common ones.

## Durations

A complete event describes an operation in one event, once it is done: see
[Getting started](02-GettingStarted.md#measure-an-operation). Begin and end events describe it in two events, so the
start can be written before the operation ends:

<!-- ref:../../tests/Meziantou.Framework.ChromiumTracing.Tests/WriterTests.cs;region=ChromiumTracing_NestedDurationEvents;format=md-fenced-code-block;dedent=true -->
```csharp
using var stream = new MemoryStream();
await using (var writer = new ChromiumTracingWriter(stream))
{
    var start = DateTimeOffset.UtcNow;

    // The events of a thread must be ordered: an End event closes the last Begin event of the thread
    await writer.WriteEventAsync(new ChromiumTracingDurationBeginEvent { Name = "Build", Timestamp = start, ProcessId = 1, ThreadId = 1 });
    await writer.WriteEventAsync(new ChromiumTracingDurationBeginEvent { Name = "Restore", Timestamp = start.AddMilliseconds(10), ProcessId = 1, ThreadId = 1 });
    await writer.WriteEventAsync(new ChromiumTracingDurationEndEvent { Timestamp = start.AddMilliseconds(40), ProcessId = 1, ThreadId = 1 });
    await writer.WriteEventAsync(new ChromiumTracingDurationEndEvent { Timestamp = start.AddMilliseconds(100), ProcessId = 1, ThreadId = 1 });
}

using var trace = JsonDocument.Parse(stream.ToArray());
Assert.Equal(["B", "B", "E", "E"], trace.RootElement.EnumerateArray().Select(item => item.GetProperty("ph").GetString()).ToArray());
```
<!-- endref -->

The durations of a thread nest: the viewer draws `Restore` under `Build`. An end event closes the last open begin event
of its process and thread, so the example does not give it a name. Complete events nest the same way when one lies
within another.

A complete event has a `Duration`, and optionally a `ThreadDuration` (the time the thread actually ran) and an
`EndStackTrace`.

## Instants and counters

<!-- ref:../../tests/Meziantou.Framework.ChromiumTracing.Tests/WriterTests.cs;region=ChromiumTracing_InstantAndCounterEvents;format=md-fenced-code-block;dedent=true -->
```csharp
using var stream = new MemoryStream();
await using (var writer = new ChromiumTracingWriter(stream))
{
    var start = DateTimeOffset.UtcNow;

    await writer.WriteEventAsync(new ChromiumTracingInstantEvent
    {
        Name = "Cache cleared",
        Timestamp = start,
        Scope = ChromiumTracingInstantEventScope.Global, // a line across the whole timeline
        ProcessId = 1,
        ThreadId = 1,
    });

    // Each argument of a counter event is a series of the same chart
    await writer.WriteEventAsync(new ChromiumTracingCounterEvent
    {
        Name = "Cache",
        Timestamp = start,
        ProcessId = 1,
        Arguments = new Dictionary<string, object?>(StringComparer.Ordinal) { ["hits"] = 0, ["misses"] = 0 },
    });
    await writer.WriteEventAsync(new ChromiumTracingCounterEvent
    {
        Name = "Cache",
        Timestamp = start.AddSeconds(1),
        ProcessId = 1,
        Arguments = new Dictionary<string, object?>(StringComparer.Ordinal) { ["hits"] = 120, ["misses"] = 8 },
    });
}

using var trace = JsonDocument.Parse(stream.ToArray());
Assert.Equal(["i", "C", "C"], trace.RootElement.EnumerateArray().Select(item => item.GetProperty("ph").GetString()).ToArray());
```
<!-- endref -->

- The `Scope` of an instant event is `Thread`, `Process` or `Global`: the mark is drawn on the row of the thread, of the
  process, or across the whole timeline.
- A counter event gives the values of a counter at its timestamp. Each argument is a series of the chart; the values
  are numbers. The `Id` property of a counter event, when set, makes the counter distinct from the counters with the same
  name.

## Asynchronous operations

Operations that overlap on the same thread, such as concurrent downloads, cannot be nested durations. Asynchronous
events describe them with an `Id`: the events with the same category and id belong to the same operation.

<!-- ref:../../tests/Meziantou.Framework.ChromiumTracing.Tests/WriterTests.cs;region=ChromiumTracing_OverlappingAsyncEvents;format=md-fenced-code-block;dedent=true -->
```csharp
using var stream = new MemoryStream();
await using (var writer = new ChromiumTracingWriter(stream))
{
    var start = DateTimeOffset.UtcNow;

    // Two downloads run at the same time: the Id tells them apart
    await writer.WriteEventAsync(new ChromiumTracingAsyncBeginEvent { Name = "Download", Category = "http", Id = 1, Timestamp = start, ProcessId = 1 });
    await writer.WriteEventAsync(new ChromiumTracingAsyncBeginEvent { Name = "Download", Category = "http", Id = 2, Timestamp = start.AddMilliseconds(5), ProcessId = 1 });
    await writer.WriteEventAsync(new ChromiumTracingAsyncInstantEvent { Name = "Headers received", Category = "http", Id = 1, Timestamp = start.AddMilliseconds(20), ProcessId = 1 });
    await writer.WriteEventAsync(new ChromiumTracingAsyncEndEvent { Name = "Download", Category = "http", Id = 2, Timestamp = start.AddMilliseconds(50), ProcessId = 1 });
    await writer.WriteEventAsync(new ChromiumTracingAsyncEndEvent { Name = "Download", Category = "http", Id = 1, Timestamp = start.AddMilliseconds(80), ProcessId = 1 });
}

using var trace = JsonDocument.Parse(stream.ToArray());
Assert.Equal(["b", "b", "n", "e", "e"], trace.RootElement.EnumerateArray().Select(item => item.GetProperty("ph").GetString()).ToArray());
```
<!-- endref -->

`ChromiumTracingAsyncInstantEvent` marks a point in time within the operation.

## Flows

Flow events draw arrows from one event to another, for instance from the code that queues a work item to the thread
that runs it. `ChromiumTracingFlowBeginEvent` starts the flow, `ChromiumTracingFlowStepEvent` adds a step, and
`ChromiumTracingFlowEndEvent` ends it. The begin and step events are attached to the duration that encloses them on
their thread. The `BindingPoint` of the end event chooses the duration the arrow points to: `NextSlice`, the default, is
the next duration that starts on its thread, and `EnclosingSlice` the one that encloses it.

The flow event types do not have the `id` field that the format uses to tell the flows apart, so they cannot describe
several flows that overlap.

## Objects

Object events show the lifetime of an object, identified by its `Id`, and snapshots of its state in their arguments:

<!-- ref:../../tests/Meziantou.Framework.ChromiumTracing.Tests/WriterTests.cs;region=ChromiumTracing_ObjectLifecycle;format=md-fenced-code-block;dedent=true -->
```csharp
using var stream = new MemoryStream();
await using (var writer = new ChromiumTracingWriter(stream))
{
    var start = DateTimeOffset.UtcNow;

    await writer.WriteEventAsync(new ChromiumTracingObjectCreatedEvent { Name = "Connection", Id = "connection-1", Timestamp = start, ProcessId = 1 });
    await writer.WriteEventAsync(new ChromiumTracingObjectSnapshotEvent
    {
        Name = "Connection",
        Id = "connection-1",
        Timestamp = start.AddMilliseconds(10),
        ProcessId = 1,
        Arguments = new Dictionary<string, object?>(StringComparer.Ordinal) { ["state"] = "open" },
    });
    await writer.WriteEventAsync(new ChromiumTracingObjectDestroyedEvent { Name = "Connection", Id = "connection-1", Timestamp = start.AddMilliseconds(30), ProcessId = 1 });
}

using var trace = JsonDocument.Parse(stream.ToArray());
Assert.Equal(["N", "O", "D"], trace.RootElement.EnumerateArray().Select(item => item.GetProperty("ph").GetString()).ToArray());
```
<!-- endref -->

## Names of the processes and threads

By default, the viewer shows the numbers of the processes and threads. Metadata events give them names and choose their
order:

<!-- ref:../../tests/Meziantou.Framework.ChromiumTracing.Tests/WriterTests.cs;region=ChromiumTracing_NameProcessesAndThreads;format=md-fenced-code-block;dedent=true -->
```csharp
using var stream = new MemoryStream();
await using (var writer = new ChromiumTracingWriter(stream))
{
    await writer.WriteEventAsync(ChromiumTracingMetadataEvent.ProcessName(pid: 1, "Build server"));
    await writer.WriteEventAsync(ChromiumTracingMetadataEvent.ProcessSortIndex(pid: 1, index: 0));
    await writer.WriteEventAsync(ChromiumTracingMetadataEvent.ThreadName(pid: 1, tid: 1, "Main thread"));
    await writer.WriteEventAsync(ChromiumTracingMetadataEvent.ThreadName(pid: 1, tid: 2, "Worker"));
}

using var trace = JsonDocument.Parse(stream.ToArray());
Assert.Equal(
    ["process_name", "process_sort_index", "thread_name", "thread_name"],
    trace.RootElement.EnumerateArray().Select(item => item.GetProperty("name").GetString()).ToArray());
```
<!-- endref -->

| Method | Sets |
| --- | --- |
| `ProcessName(pid, name)` | The name of a process |
| `ProcessLabels(pid, labels)` | The labels of a process |
| `ProcessSortIndex(pid, index)` | The position of a process; lower indexes come first |
| `ThreadName(pid, tid, name)` | The name of a thread |
| `ThreadSortIndex(pid, tid, index)` | The position of a thread within its process |
