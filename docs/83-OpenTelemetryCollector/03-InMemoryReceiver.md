---
title: Testing with the in-memory receiver
description: Keep the received export requests in memory, read them in tests, limit how many are kept, and control their time.
---

`InMemoryOpenTelemetryHandler` keeps the export requests it receives in memory, so a test can check the telemetry that an
application exported. Register it with `AddInMemoryOpenTelemetryReceiver`, and get it from the service provider of the
application that hosts the receiver: see [Getting started](02-GettingStarted.md#receive-an-export-request).

## Read the received items

`Logs`, `Traces` and `Metrics` return the items received so far, in the order they were received, as
`OpenTelemetryItem`s. Each item is one export request:

| Item | `Request` |
| --- | --- |
| `OpenTelemetryLogsItem` | `ExportLogsServiceRequest` |
| `OpenTelemetryTracesItem` | `ExportTraceServiceRequest` |
| `OpenTelemetryMetricsItem` | `ExportMetricsServiceRequest` |

Use `OfType<OpenTelemetryLogsItem>()`, or the matching type, to get the `Request`, then walk the resources and scopes
down to the records:

<!-- ref:../../tests/Meziantou.Framework.OpenTelemetry.Tests/OpenTelemetryReceiverTests.cs;region=OpenTelemetryCollector_OtlpJson;format=md-fenced-code-block;dedent=true -->
```csharp
var builder = WebApplication.CreateBuilder();
builder.WebHost.UseTestServer();
builder.Services.AddInMemoryOpenTelemetryReceiver();

await using var app = builder.Build();
app.MapOpenTelemetryReceiverEndpoints();
await app.StartAsync();

// OTLP/JSON: trace and span ids are hexadecimal strings
using var httpClient = app.GetTestClient();
using var content = new StringContent("""
    {"resourceSpans":[{"scopeSpans":[{"spans":[
      {"traceId":"5b8efff798038103d269b633813fc60c","spanId":"eee19b7ec3c1b174","name":"GET /orders"}
    ]}]}]}
    """, Encoding.UTF8, "application/json");
using var response = await httpClient.PostAsync(new Uri("/v1/traces", UriKind.Relative), content);

// The response uses the encoding of the request
Assert.Equal(HttpStatusCode.OK, response.StatusCode);
Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

var receiver = app.Services.GetRequiredService<InMemoryOpenTelemetryHandler>();
var span = receiver.Traces.OfType<OpenTelemetryTracesItem>()
    .SelectMany(item => item.Request.ResourceSpans)
    .SelectMany(resourceSpans => resourceSpans.ScopeSpans)
    .SelectMany(scopeSpans => scopeSpans.Spans)
    .Single();
Assert.Equal("GET /orders", span.Name);
Assert.Equal("5b8efff798038103d269b633813fc60c", Convert.ToHexStringLower(span.TraceId.Span));
```
<!-- endref -->

Every item also has:

- `ItemType`: `Logs`, `Traces` or `Metrics`.
- `Method`: what carried the request, such as `POST /v1/traces` over HTTP, or
  `/opentelemetry.proto.collector.trace.v1.TraceService/Export` over gRPC.
- `ReceivedAt`: when the request was received.

The stored request is a copy of the received one. The messages only have some of the OTLP fields: see
[the available fields](04-CustomHandlers.md#available-fields).

The collections can be read while requests are received: enumerating them returns the items received until then.

## Limit the number of items

By default nothing is ever removed: every export request is kept. That suits a test, but a long-running process must set
limits, otherwise its memory grows with the received telemetry. Set `MaximumLogCount`, `MaximumTraceCount` and
`MaximumMetricCount` in an `InMemoryOpenTelemetryHandlerOptions`, and pass it to `AddInMemoryOpenTelemetryReceiver`.
Once a limit is reached, each new item replaces the oldest one.

The handler can also be created and called directly, without hosting the endpoints, for instance to test code that reads
its items:

<!-- ref:../../tests/Meziantou.Framework.OpenTelemetry.Tests/OpenTelemetryReceiverTests.cs;region=OpenTelemetryCollectorInMemory_RetentionAndTime;format=md-fenced-code-block;dedent=true -->
```csharp
var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
var receiver = new InMemoryOpenTelemetryHandler(new InMemoryOpenTelemetryHandlerOptions { MaximumLogCount = 2 }, timeProvider);
var context = new OpenTelemetryHandlerContext(OpenTelemetryTransport.Http, "POST /v1/logs", new OpenTelemetryPartialSuccess());

foreach (var message in new[] { "first", "second", "third" })
{
    var exportRequest = new ExportLogsServiceRequest
    {
        ResourceLogs = { new ResourceLogs { ScopeLogs = { new ScopeLogs { LogRecords = { new LogRecord { Body = new AnyValue { StringValue = message } } } } } } },
    };

    await receiver.HandleLogsAsync(context, exportRequest, CancellationToken.None);
    timeProvider.Advance(TimeSpan.FromSeconds(1));
}

// Only the 2 most recent export requests are kept
var items = receiver.Logs.OfType<OpenTelemetryLogsItem>().ToList();
Assert.Equal(["second", "third"], items.Select(item => item.Request.ResourceLogs[0].ScopeLogs[0].LogRecords[0].Body.StringValue).ToArray());

// ReceivedAt comes from the TimeProvider
Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 0, 1, TimeSpan.Zero), items[0].ReceivedAt);
```
<!-- endref -->

- The limits count export requests, not records: a request can hold many log records, spans or metrics.
- A limit must be greater than 0. The default, `int.MaxValue`, keeps every item.

## Control the time

`ReceivedAt` comes from the `TimeProvider` of the handler. `AddInMemoryOpenTelemetryReceiver` uses the `TimeProvider`
registered in the service collection, `TimeProvider.System` when there is none: register a `FakeTimeProvider` (from the
`Microsoft.Extensions.TimeProvider.Testing` package) to control it, as the example above does with the constructor.

## Overloads

| Method | Registers |
| --- | --- |
| `AddInMemoryOpenTelemetryReceiver()` | The in-memory handler, without limits |
| `AddInMemoryOpenTelemetryReceiver(options)` | The in-memory handler, with the limits of an `InMemoryOpenTelemetryHandlerOptions` |
| `AddInMemoryOpenTelemetryReceiver(configure)` | The in-memory handler, and configures the `OpenTelemetryReceiverOptions` |
| `AddInMemoryOpenTelemetryReceiver(options, configure)` | Both |

`OpenTelemetryReceiverOptions` configure the endpoints and the samplers. See [Endpoints](05-Endpoints.md) and
[Sampling](06-Sampling.md).

The in-memory handler can be combined with other handlers: every registered handler receives every request. See
[Custom handlers](04-CustomHandlers.md#several-handlers).
