---
title: Files and streams
description: Write a trace to a file or a stream, choose who disposes the stream, compress the trace, and write from several threads.
---

## Write to a file

`ChromiumTracingWriter.Create(path)` creates the file, or overwrites an existing one, and closes it when the writer is
disposed. See [Getting started](02-GettingStarted.md#write-a-trace-file).

## Write to a stream

The constructor writes to a stream that you keep: disposing the writer does not dispose the stream.

<!-- ref:../../tests/Meziantou.Framework.ChromiumTracing.Tests/WriterTests.cs;region=ChromiumTracing_WriteToStream;format=md-fenced-code-block;dedent=true -->
```csharp
using var stream = new MemoryStream();
await using (var writer = new ChromiumTracingWriter(stream)) // the constructor does not dispose the stream
{
    await writer.WriteEventAsync(new ChromiumTracingInstantEvent
    {
        Name = "Started",
        Timestamp = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        Scope = ChromiumTracingInstantEventScope.Process,
        ProcessId = 1,
        ThreadId = 1,
    });
}

// Disposing the writer closes the JSON array. Timestamps are in microseconds.
Assert.Equal("""
    [
    {"ph":"i","s":"p","name":"Started","ts":63902822400000000,"pid":1,"tid":1}
    ]
    """, Encoding.UTF8.GetString(stream.ToArray()));
```
<!-- endref -->

The trace is a JSON array, with one event per line. Disposing the writer writes the closing bracket: a trace is only a
valid JSON document once the writer is disposed. A writer disposed without any event writes `[]`.

The factory methods choose who disposes the stream:

| Method | Disposes the stream |
| --- | --- |
| `new ChromiumTracingWriter(stream)` | No |
| `ChromiumTracingWriter.Create(stream)` | Yes |
| `ChromiumTracingWriter.Create(stream, streamOwned)` | When `streamOwned` is `true` |
| `ChromiumTracingWriter.Create(path)` | Yes: the writer owns the file |

## Compress the trace

Traces of long runs can be large. [CreateGzip](cref:Meziantou.Framework.ChromiumTracing.ChromiumTracingWriter.CreateGzip) compresses the trace with GZip as it is written. The compression level
is `CompressionLevel.Fastest` by default (`CompressionLevel` is in the `System.IO.Compression` namespace):

<!-- ref:../../tests/Meziantou.Framework.ChromiumTracing.Tests/WriterTests.cs;region=ChromiumTracing_Compression;format=md-fenced-code-block;dedent=true -->
```csharp
// Create compressed file
await using var writer = ChromiumTracingWriter.CreateGzip(
    "trace.json.gz",
    CompressionLevel.Fastest);

await writer.WriteEventAsync(new ChromiumTracingCompleteEvent
{
    Name = "Operation",
    Timestamp = DateTimeOffset.UtcNow,
    Duration = TimeSpan.FromMilliseconds(100),
});
```
<!-- endref -->

`CreateGzip(stream)` writes the compressed trace to a stream. Disposing the writer completes the compressed data, but does
not dispose the stream:

<!-- ref:../../tests/Meziantou.Framework.ChromiumTracing.Tests/WriterTests.cs;region=ChromiumTracing_GzipStream;format=md-fenced-code-block;dedent=true -->
```csharp
using var stream = new MemoryStream();
await using (var writer = ChromiumTracingWriter.CreateGzip(stream, CompressionLevel.Optimal))
{
    await writer.WriteEventAsync(new ChromiumTracingInstantEvent { Name = "Compressed" });
}

// The writer disposes the GZipStream it created, but not the stream it wraps
stream.Position = 0;
await using var decompressed = new GZipStream(stream, CompressionMode.Decompress);
using var trace = await JsonDocument.ParseAsync(decompressed);
Assert.Equal("Compressed", trace.RootElement[0].GetProperty("name").GetString());
```
<!-- endref -->

## Write from several threads

[WriteEventAsync](cref:Meziantou.Framework.ChromiumTracing.ChromiumTracingWriter.WriteEventAsync) can be called from several threads at the same time. The writes are serialized, and each event is
written whole, so the trace stays a valid document. The events are written in the order the calls get the writer, not
in the order of their timestamps: the format does not require the events to be sorted.

## Errors

- [WriteEventAsync](cref:Meziantou.Framework.ChromiumTracing.ChromiumTracingWriter.WriteEventAsync) serializes the event before writing it: when the serialization fails, for instance for an
  [argument of an unknown type](04-EventProperties.md#arguments), nothing is written and the trace stays valid.
- [WriteEventAsync](cref:Meziantou.Framework.ChromiumTracing.ChromiumTracingWriter.WriteEventAsync) throws an `ObjectDisposedException` once the writer is disposed.
- Disposing the writer twice does nothing the second time. When the closing bracket cannot be written, the stream is
  still disposed if the writer owns it.
