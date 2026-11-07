---
title: Event properties
description: Set the name, category, time, process, thread, color, stack trace and arguments of an event, and serialize arguments of your own types.
---

## Common properties

Every event has the properties of [ChromiumTracingEvent](cref:Meziantou.Framework.ChromiumTracing.ChromiumTracingEvent). Each one is written to a field of the format, and the
properties left to `null` are not written:

<!-- ref:../../tests/Meziantou.Framework.ChromiumTracing.Tests/WriterTests.cs;region=ChromiumTracing_CommonProperties;format=md-fenced-code-block;dedent=true -->
```csharp
using var stream = new MemoryStream();
await using (var writer = new ChromiumTracingWriter(stream))
{
    await writer.WriteEventAsync(new ChromiumTracingCompleteEvent
    {
        Name = "Parse",
        Category = "compiler,frontend",                    // several categories, separated by commas
        Timestamp = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        Duration = TimeSpan.FromMilliseconds(1.5),
        ProcessId = 1,
        ThreadId = 2,
        ColorName = "good",                                // a color name of the trace viewer
        StackTrace = ["Program.Main", "Compiler.Parse"],
        Arguments = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["file"] = "Program.cs",
            ["lines"] = 120,
        },
    });
}

Assert.Equal("""
    [
    {"ph":"X","dur":1500,"name":"Parse","cat":"compiler,frontend","ts":63902822400000000,"pid":1,"tid":2,"cname":"good","stack":["Program.Main","Compiler.Parse"],"args":{"file":"Program.cs","lines":120}}
    ]
    """, Encoding.UTF8.GetString(stream.ToArray()));
```
<!-- endref -->

| Property | Field | Description |
| --- | --- | --- |
| [Name](cref:Meziantou.Framework.ChromiumTracing.ChromiumTracingEvent.Name) | `name` | The name of the event, shown on the timeline |
| [Category](cref:Meziantou.Framework.ChromiumTracing.ChromiumTracingEvent.Category) | `cat` | The category of the event; separate several categories with commas. Viewers can filter the events by category |
| [Timestamp](cref:Meziantou.Framework.ChromiumTracing.ChromiumTracingEvent.Timestamp) | `ts` | When the event occurred |
| [ThreadTimestamp](cref:Meziantou.Framework.ChromiumTracing.ChromiumTracingEvent.ThreadTimestamp) | `tts` | The thread clock when the event occurred |
| [ProcessId](cref:Meziantou.Framework.ChromiumTracing.ChromiumTracingEvent.ProcessId) | `pid` | The process of the event |
| [ThreadId](cref:Meziantou.Framework.ChromiumTracing.ChromiumTracingEvent.ThreadId) | `tid` | The thread of the event |
| [ColorName](cref:Meziantou.Framework.ChromiumTracing.ChromiumTracingEvent.ColorName) | `cname` | The name of a color of the viewer's palette |
| [StackTrace](cref:Meziantou.Framework.ChromiumTracing.ChromiumTracingEvent.StackTrace) | `stack` | The stack trace of the event, as a list of frames |
| [Arguments](cref:Meziantou.Framework.ChromiumTracing.ChromiumTracingEvent.Arguments) | `args` | Data attached to the event, shown when you select it |

Some event types add their own properties: [Duration](cref:Meziantou.Framework.ChromiumTracing.ChromiumTracingCompleteEvent.Duration) for a complete event, `Id` for asynchronous, counter and object
events, [Scope](cref:Meziantou.Framework.ChromiumTracing.ChromiumTracingInstantEvent.Scope) for an instant event. See [Event types](03-EventTypes.md).

## Timestamps and durations

[Timestamp](cref:Meziantou.Framework.ChromiumTracing.ChromiumTracingEvent.Timestamp) and [ThreadTimestamp](cref:Meziantou.Framework.ChromiumTracing.ChromiumTracingEvent.ThreadTimestamp) are `DateTimeOffset` values, written in microseconds from the UTC time. The offset is
ignored: the same instant gives the same value, whatever the time zone of the `DateTimeOffset`. [Duration](cref:Meziantou.Framework.ChromiumTracing.ChromiumTracingCompleteEvent.Duration) and
[ThreadDuration](cref:Meziantou.Framework.ChromiumTracing.ChromiumTracingCompleteEvent.ThreadDuration) are `TimeSpan` values, written in microseconds too, so a precision finer than a microsecond is lost.

Use `DateTimeOffset.UtcNow` for the start of an operation, and a `Stopwatch` for its duration: the clock can be adjusted
while the operation runs, the elapsed time of a `Stopwatch` cannot.

## Arguments

[Arguments](cref:Meziantou.Framework.ChromiumTracing.ChromiumTracingEvent.Arguments) is an `IReadOnlyDictionary<string, object?>`. Its values are serialized according to their runtime type, and
the writer knows these types out of the box:

- `string`, `bool`, `char`, `Guid`, `Uri`
- `byte`, `sbyte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong`, `float`, `double`, `decimal`
- `DateTime`, `DateTimeOffset`, `TimeSpan`

[WriteEventAsync](cref:Meziantou.Framework.ChromiumTracing.ChromiumTracingWriter.WriteEventAsync) throws a `NotSupportedException` for a value of another type. The event is not written, and the trace
stays a valid JSON document.

## Arguments of your own types

To write values of other types, declare a `JsonSerializerContext` for them:

<!-- ref:../../tests/Meziantou.Framework.ChromiumTracing.Tests/WriterTests.cs;region=ChromiumTracing_CustomArgumentTypesContext;format=md-fenced-code-block;dedent=true -->
```csharp
public sealed class RequestInfo
{
    public string? Method { get; set; }
    public string? Path { get; set; }
}

[JsonSerializable(typeof(RequestInfo))]
[JsonSourceGenerationOptions(RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true)]
public sealed partial class DocumentationJsonContext : JsonSerializerContext
{
}
```
<!-- endref -->

and pass it to the writer, which combines it with its own:

<!-- ref:../../tests/Meziantou.Framework.ChromiumTracing.Tests/WriterTests.cs;region=ChromiumTracing_CustomArgumentTypes;format=md-fenced-code-block;dedent=true -->
```csharp
using var stream = new MemoryStream();

// Combine a JsonSerializerContext that knows the type with the built-in one
await using (var writer = new ChromiumTracingWriter(stream, DocumentationJsonContext.Default))
{
    await writer.WriteEventAsync(new ChromiumTracingInstantEvent
    {
        Name = "Request",
        Arguments = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["request"] = new RequestInfo { Method = "GET", Path = "/orders" },
        },
    });
}

using var trace = JsonDocument.Parse(stream.ToArray());
Assert.Equal("/orders", trace.RootElement[0].GetProperty("args").GetProperty("request").GetProperty("Path").GetString());
```
<!-- endref -->

Every factory method and constructor of [ChromiumTracingWriter](cref:Meziantou.Framework.ChromiumTracing.ChromiumTracingWriter) has an overload that takes a `JsonSerializerContext`.
The context uses source-generated metadata, so the writer stays compatible with trimming and native AOT.
