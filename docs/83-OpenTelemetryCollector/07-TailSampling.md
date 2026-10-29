---
title: Tail sampling
description: Buffer the spans of each trace until its root span arrives, keep or drop the whole trace, and bound the memory used by the buffer.
---

`OpenTelemetryTailSampler` keeps or drops traces as a whole. It buffers the spans of each trace, by trace id, and
evaluates the trace when:

- its root span, the span without a parent span id, is received;
- or the trace has been buffered for `MaxTraceDuration` without receiving its root span.

The spans of a kept trace are then passed to the handlers together, in one export request.

## Keep some traces

Add the tail sampler to `Samplers`, and give it a `ShouldSample` function that decides on a trace:

<!-- ref:../../tests/Meziantou.Framework.OpenTelemetry.Tests/OpenTelemetryReceiverTests.cs;region=OpenTelemetryCollector_TailSamplingByRootSpan;format=md-fenced-code-block;dedent=true -->
```csharp
var builder = WebApplication.CreateBuilder();
builder.WebHost.UseTestServer();
builder.Services.AddInMemoryOpenTelemetryReceiver(options =>
{
    // Keep only the traces of the checkout requests
    options.Samplers.Add(new OpenTelemetryTailSampler
    {
        MaxTraceDuration = TimeSpan.FromSeconds(30),
        ShouldSample = static (context, _) => ValueTask.FromResult(context.RootSpan?.Name is "POST /checkout"),
    });
});

await using var app = builder.Build();
app.MapOpenTelemetryReceiverEndpoints();
await app.StartAsync();
using var httpClient = app.GetTestClient();

var checkoutTraceId = ByteString.CopyFrom(Convert.FromHexString("5b8efff798038103d269b633813fc60c"));
var checkoutRootSpanId = ByteString.CopyFrom(Convert.FromHexString("eee19b7ec3c1b174"));
var healthTraceId = ByteString.CopyFrom(Convert.FromHexString("0af7651916cd43dd8448eb211c80319c"));

Span[] spans =
[
    // A child span arrives first: its trace is buffered until the root span, which has no parent span id, arrives
    new Span { TraceId = checkoutTraceId, SpanId = ByteString.CopyFrom(Convert.FromHexString("0102030405060708")), ParentSpanId = checkoutRootSpanId, Name = "SELECT cart" },
    new Span { TraceId = checkoutTraceId, SpanId = checkoutRootSpanId, Name = "POST /checkout" },
    new Span { TraceId = healthTraceId, SpanId = ByteString.CopyFrom(Convert.FromHexString("b7ad6b7169203331")), Name = "GET /health" },
];

foreach (var span in spans)
{
    var exportRequest = new ExportTraceServiceRequest { ResourceSpans = { new ResourceSpans { ScopeSpans = { new ScopeSpans { Spans = { span } } } } } };
    using var content = new ByteArrayContent(exportRequest.ToByteArray());
    content.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");
    using var response = await httpClient.PostAsync(new Uri("/v1/traces", UriKind.Relative), content);
    response.EnsureSuccessStatusCode();
}

// The spans of the checkout trace are dispatched together, in one export request; the health check trace is dropped
var receiver = app.Services.GetRequiredService<InMemoryOpenTelemetryHandler>();
var item = Assert.Single(receiver.Traces.OfType<OpenTelemetryTracesItem>());
Assert.Equal(
    ["SELECT cart", "POST /checkout"],
    item.Request.ResourceSpans.SelectMany(r => r.ScopeSpans).SelectMany(s => s.Spans).Select(s => s.Name).ToArray());
```
<!-- endref -->

`ShouldSample` receives an `OpenTelemetryTailTraceContext`:

| Property | Description |
| --- | --- |
| `TraceId` | The trace id, in uppercase hexadecimal |
| `Spans` | The buffered spans of the trace |
| `RootSpan` | The root span, or `null` when the trace timed out without it |
| `IsRootSpanObserved` | Whether the root span was received |
| `TimedOut` | Whether the trace is evaluated because it reached `MaxTraceDuration` |
| `FirstSpanReceivedAt` | When the first span of the trace was received |
| `EvaluationTime` | When the trace is evaluated |
| `HandlerContext` | The context of the last request that added spans to the trace |

Without a `ShouldSample` function, every trace is kept: the tail sampler then only groups the spans of each trace.

The spans of the dispatched request keep their resource and instrumentation scope, grouped as the clients sent them.
Spans without a trace id are dropped.

## Options

| Property | Default | Description |
| --- | --- | --- |
| `MaxTraceDuration` | 2 minutes | How long a trace is buffered while waiting for its root span |
| `MaxBufferedSpansPerTrace` | 5000 | How many spans a trace can buffer before `OverflowPolicy` applies |
| `MaxBufferedSpans` | 100,000 | How many spans can be buffered over all traces |
| `OverflowPolicy` | `DropWholeTrace` | What happens to a trace that exceeds `MaxBufferedSpansPerTrace` |
| `SweepInterval` | A quarter of `MaxTraceDuration` | How often the buffered traces are checked for reaching `MaxTraceDuration` |

The durations and limits must be greater than zero.

## Traces that never complete

A trace whose root span never arrives is evaluated once it has been buffered for `MaxTraceDuration`, with `TimedOut` set
and `RootSpan` set to `null`. A background service checks the buffered traces every `SweepInterval`, so such traces are
released even when no other request is received. The service uses the `TimeProvider` of the service collection.

## Bound the buffer

`OverflowPolicy` applies to a trace that exceeds `MaxBufferedSpansPerTrace`:

| Value | Keeps |
| --- | --- |
| `DropWholeTrace` | Nothing: the whole trace is dropped |
| `DropOldestSpans` | The newest spans |
| `DropNewestSpans` | The oldest spans |

`MaxBufferedSpans` is a separate limit, over all buffered traces. Reaching it evicts whole traces, largest first: a trace
within `MaxBufferedSpansPerTrace` is never truncated because of other traffic, and new traces are still accepted when
the buffer is full.

A trace dropped as a whole, by `DropWholeTrace` or by an eviction, is remembered for `MaxTraceDuration`: the spans of the
same trace received later are dropped too, instead of being dispatched as a fragment of the trace.

The spans dropped because a trace exceeded its own limit are reported to the client that sent them, as a
[partial success](04-CustomHandlers.md#report-rejected-records). The spans lost to an eviction are only logged: they
belong to traces buffered by earlier requests, not to the request that triggered the eviction.

## How dispatch works

- The other samplers of the list run first, on each request, whatever their position: the tail sampler only buffers the
  spans of the requests they keep. It only applies to traces: logs and metrics go to the handlers directly.
- Only one tail sampler can be added: a second one makes the receiver fail with an `InvalidOperationException`.
- A trace is dispatched by the request that completes it, or by the background service, not by the requests that sent
  its other spans. `ShouldSample` and the handlers receive `CancellationToken.None`, so a client that aborts its request
  does not discard the spans that other requests buffered. The partial successes reported once the client got its
  response are ignored.
- A handler that throws while a buffered trace is dispatched is logged and skipped: the failure never fails the request
  that triggered the dispatch, and the other traces are still dispatched.
