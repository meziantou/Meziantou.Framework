# Meziantou.Framework.ChromiumTracing

A .NET library for generating Chromium Trace Event Format files that can be visualized in Chrome's `chrome://tracing` viewer and other compatible tools.

## Features

- Write trace events in the [Chromium Trace Event Format](https://docs.google.com/document/d/1CvAClvFfyA5R-PhYUmn5OOQtYMH4h6I0nSsKchNAySU/preview)
- Support for all major event types (Duration, Complete, Instant, Async, Flow, Counter, Object, Metadata, etc.)
- AOT and trimming compatible with source-generated JSON serialization
- Optional GZip compression support
- Async/await support for efficient I/O operations

## Usage

### Basic Example

```csharp
using Meziantou.Framework.ChromiumTracing;

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
        ["count"] = 42
    }
});
```

The trace file can be viewed by opening `chrome://tracing` in Chrome or Edge and loading the generated file.

### Event Types

#### Duration Events

Track the beginning and end of operations separately:

```csharp
// Begin event
await writer.WriteEventAsync(new ChromiumTracingDurationBeginEvent
{
    Name = "Processing",
    Category = "compute",
    Timestamp = DateTimeOffset.UtcNow,
    ProcessId = Environment.ProcessId,
    ThreadId = Environment.CurrentManagedThreadId
});

// ... do work ...

// End event
await writer.WriteEventAsync(new ChromiumTracingDurationEndEvent
{
    Name = "Processing",
    Category = "compute",
    Timestamp = DateTimeOffset.UtcNow,
    ProcessId = Environment.ProcessId,
    ThreadId = Environment.CurrentManagedThreadId
});
```

#### Complete Events

Track an operation with a single event (includes start time and duration):

```csharp
await writer.WriteEventAsync(new ChromiumTracingCompleteEvent
{
    Name = "Complete Operation",
    Category = "work",
    Timestamp = startTime,
    Duration = TimeSpan.FromMilliseconds(200),
    ProcessId = Environment.ProcessId,
    ThreadId = Environment.CurrentManagedThreadId
});
```

#### Instant Events

Mark a single point in time:

```csharp
await writer.WriteEventAsync(new ChromiumTracingInstantEvent
{
    Name = "Checkpoint",
    Category = "milestone",
    Timestamp = DateTimeOffset.UtcNow,
    Scope = ChromiumTracingInstantEventScope.Thread, // Global, Process, or Thread
    ProcessId = Environment.ProcessId,
    ThreadId = Environment.CurrentManagedThreadId
});
```

#### Counter Events

Display counter values over time:

```csharp
await writer.WriteEventAsync(new ChromiumTracingCounterEvent
{
    Name = "Memory Usage",
    Category = "system",
    Timestamp = DateTimeOffset.UtcNow,
    ProcessId = Environment.ProcessId,
    Arguments = new Dictionary<string, object?>
    {
        ["bytes"] = 1024 * 1024 * 500 // 500 MB
    }
});
```

#### Async Events

Track asynchronous operations:

```csharp
// Start async operation
await writer.WriteEventAsync(new ChromiumTracingAsyncBeginEvent
{
    Name = "Async Task",
    Category = "async",
    Timestamp = DateTimeOffset.UtcNow,
    Id = 123, // Unique ID for this async operation
    ProcessId = Environment.ProcessId
});

// End async operation
await writer.WriteEventAsync(new ChromiumTracingAsyncEndEvent
{
    Name = "Async Task",
    Category = "async",
    Timestamp = DateTimeOffset.UtcNow,
    Id = 123, // Same ID as begin event
    ProcessId = Environment.ProcessId
});

// Instant async event
await writer.WriteEventAsync(new ChromiumTracingAsyncInstantEvent
{
    Name = "Async Milestone",
    Category = "async",
    Timestamp = DateTimeOffset.UtcNow,
    Id = 123,
    ProcessId = Environment.ProcessId
});
```

#### Flow Events

Visualize flow between events:

```csharp
// Start flow
await writer.WriteEventAsync(new ChromiumTracingFlowBeginEvent
{
    Name = "Request Flow",
    Category = "network",
    Timestamp = DateTimeOffset.UtcNow,
    ProcessId = Environment.ProcessId
});

// Flow step
await writer.WriteEventAsync(new ChromiumTracingFlowStepEvent
{
    Name = "Request Flow",
    Category = "network",
    Timestamp = DateTimeOffset.UtcNow,
    ProcessId = Environment.ProcessId
});

// End flow
await writer.WriteEventAsync(new ChromiumTracingFlowEndEvent
{
    Name = "Request Flow",
    Category = "network",
    Timestamp = DateTimeOffset.UtcNow,
    ProcessId = Environment.ProcessId
});
```

#### Object Events

Track object lifecycle:

```csharp
// Object created
await writer.WriteEventAsync(new ChromiumTracingObjectCreatedEvent
{
    Name = "MyObject",
    Category = "objects",
    Timestamp = DateTimeOffset.UtcNow,
    Id = "obj-1",
    ProcessId = Environment.ProcessId
});

// Object snapshot
await writer.WriteEventAsync(new ChromiumTracingObjectSnapshotEvent
{
    Name = "MyObject",
    Category = "objects",
    Timestamp = DateTimeOffset.UtcNow,
    Id = "obj-1",
    ProcessId = Environment.ProcessId,
    Arguments = new Dictionary<string, object?>
    {
        ["state"] = "active"
    }
});

// Object destroyed
await writer.WriteEventAsync(new ChromiumTracingObjectDestroyedEvent
{
    Name = "MyObject",
    Category = "objects",
    Timestamp = DateTimeOffset.UtcNow,
    Id = "obj-1",
    ProcessId = Environment.ProcessId
});
```

#### Metadata Events

Add process and thread names for better visualization:

```csharp
// Set process name
await writer.WriteEventAsync(ChromiumTracingMetadataEvent.ProcessName(
    Environment.ProcessId,
    "My Application"
));

// Set process labels
await writer.WriteEventAsync(ChromiumTracingMetadataEvent.ProcessLabels(
    Environment.ProcessId,
    "label1", "label2"
));

// Set process sort index
await writer.WriteEventAsync(ChromiumTracingMetadataEvent.ProcessSortIndex(
    Environment.ProcessId,
    1
));

// Set thread name
await writer.WriteEventAsync(ChromiumTracingMetadataEvent.ThreadName(
    Environment.ProcessId,
    Environment.CurrentManagedThreadId,
    "Main Thread"
));

// Set thread sort index
await writer.WriteEventAsync(ChromiumTracingMetadataEvent.ThreadSortIndex(
    Environment.ProcessId,
    Environment.CurrentManagedThreadId,
    0
));
```

#### Memory Dump Events

Track memory allocations:

```csharp
// Process memory dump
await writer.WriteEventAsync(new ChromiumTracingMemoryDumpProcessEvent
{
    Timestamp = DateTimeOffset.UtcNow,
    ProcessId = Environment.ProcessId
});

// Global memory dump
await writer.WriteEventAsync(new ChromiumTracingMemoryDumpGlobalEvent
{
    Timestamp = DateTimeOffset.UtcNow
});
```

#### Other Events

```csharp
// Mark event
await writer.WriteEventAsync(new ChromiumTracingMarkEvent
{
    Name = "Mark Point",
    Category = "marks",
    Timestamp = DateTimeOffset.UtcNow,
    ProcessId = Environment.ProcessId
});

// Clock sync event
await writer.WriteEventAsync(new ChromiumTracingClockSyncEvent
{
    Timestamp = DateTimeOffset.UtcNow,
    ProcessId = Environment.ProcessId
});

// Context events (for tracking nested scopes)
await writer.WriteEventAsync(new ChromiumTracingContextBeginEvent
{
    Name = "Context",
    Category = "context",
    Timestamp = DateTimeOffset.UtcNow,
    ProcessId = Environment.ProcessId
});

await writer.WriteEventAsync(new ChromiumTracingContextEndEvent
{
    Name = "Context",
    Category = "context",
    Timestamp = DateTimeOffset.UtcNow,
    ProcessId = Environment.ProcessId
});
```

### Compression

Create GZip-compressed trace files to reduce file size:

```csharp
// Create compressed file
await using var writer = ChromiumTracingWriter.CreateGzip(
    "trace.json.gz",
    CompressionLevel.Fastest
);

await writer.WriteEventAsync(new ChromiumTracingCompleteEvent
{
    Name = "Operation",
    Timestamp = DateTimeOffset.UtcNow,
    Duration = TimeSpan.FromMilliseconds(100)
});
```

## Viewing Traces

1. Open Chrome or Edge browser
2. Navigate to `chrome://tracing` or `edge://tracing`
3. Click "Load" and select your trace file
4. Use the viewer to analyze your application's performance

## Additional Resources

- [Chromium Trace Event Format Specification](https://docs.google.com/document/d/1CvAClvFfyA5R-PhYUmn5OOQtYMH4h6I0nSsKchNAySU/preview)
- [Catapult Trace Event Importer](https://github.com/catapult-project/catapult/blob/main/tracing/tracing/extras/importer/trace_event_importer.html)
