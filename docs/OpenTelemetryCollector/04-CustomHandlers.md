---
title: Custom handlers
description: Handle the received logs, traces and metrics in code, register several handlers, report rejected records, and unit test a handler.
---

## Write a handler

A handler derives from [OpenTelemetryHandler](cref:Meziantou.Framework.OpenTelemetryCollector.OpenTelemetryHandler) and overrides [HandleLogsAsync](cref:Meziantou.Framework.OpenTelemetryCollector.OpenTelemetryHandler.HandleLogsAsync), [HandleTracesAsync](cref:Meziantou.Framework.OpenTelemetryCollector.OpenTelemetryHandler.HandleTracesAsync) and
[HandleMetricsAsync](cref:Meziantou.Framework.OpenTelemetryCollector.OpenTelemetryHandler.HandleMetricsAsync). Each method receives one export request, as the client sent it:

<!-- ref:../../tests/Meziantou.Framework.OpenTelemetry.Tests/OpenTelemetryReceiverTests.cs;region=OpenTelemetryCollector_SpanNameCollector;format=md-fenced-code-block;dedent=true -->
```csharp
public sealed class SpanNameCollector : OpenTelemetryHandler
{
    public ConcurrentQueue<string> SpanNames { get; } = new();

    public override ValueTask HandleTracesAsync(OpenTelemetryHandlerContext context, ExportTraceServiceRequest request, CancellationToken cancellationToken)
    {
        foreach (var span in request.ResourceSpans.SelectMany(r => r.ScopeSpans).SelectMany(s => s.Spans))
        {
            SpanNames.Enqueue(span.Name);
        }

        return ValueTask.CompletedTask;
    }

    public override ValueTask HandleLogsAsync(OpenTelemetryHandlerContext context, ExportLogsServiceRequest request, CancellationToken cancellationToken)
        => ValueTask.CompletedTask;

    public override ValueTask HandleMetricsAsync(OpenTelemetryHandlerContext context, ExportMetricsServiceRequest request, CancellationToken cancellationToken)
        => ValueTask.CompletedTask;
}
```
<!-- endref -->

Register it with [AddOpenTelemetryReceiver\<T\>](cref:Meziantou.Framework.OpenTelemetryCollector.OpenTelemetryServiceCollectionExtensions.AddOpenTelemetryReceiver):

<!-- ref:../../tests/Meziantou.Framework.OpenTelemetry.Tests/OpenTelemetryReceiverTests.cs;region=OpenTelemetryCollector_UseCustomHandler;format=md-fenced-code-block;dedent=true -->
```csharp
var builder = WebApplication.CreateBuilder();
builder.WebHost.UseTestServer();

// The handler is registered as a singleton, and gets its dependencies from the service provider
builder.Services.AddOpenTelemetryReceiver<SpanNameCollector>();

await using var app = builder.Build();
app.MapOpenTelemetryReceiverEndpoints();
await app.StartAsync();

var exportRequest = new ExportTraceServiceRequest
{
    ResourceSpans = { new ResourceSpans { ScopeSpans = { new ScopeSpans { Spans = { new Span { Name = "GET /orders" } } } } } },
};

using var httpClient = app.GetTestClient();
using var content = new ByteArrayContent(exportRequest.ToByteArray());
content.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");
using var response = await httpClient.PostAsync(new Uri("/v1/traces", UriKind.Relative), content);

Assert.Equal(["GET /orders"], app.Services.GetRequiredService<SpanNameCollector>().SpanNames.ToArray());
```
<!-- endref -->

- The handler is a singleton, created by the service provider: its constructor can take services. It handles requests
  in parallel, so its state must be thread-safe.
- The HTTP response, or the gRPC response, is sent once every handler has returned. The `cancellationToken` is canceled
  when the client aborts the request.
- `AddOpenTelemetryReceiver(serviceProvider => handler)` registers a handler created by a factory, for instance an
  instance you created yourself.

## Several handlers

Every registered handler receives every export request, one after the other, in the order of registration. A handler
registered twice, with the same type through [AddOpenTelemetryReceiver\<T\>](cref:Meziantou.Framework.OpenTelemetryCollector.OpenTelemetryServiceCollectionExtensions.AddOpenTelemetryReceiver) or as the same instance through the factory
overload, receives each request once.

## The context of a request

[OpenTelemetryHandlerContext](cref:Meziantou.Framework.OpenTelemetryCollector.OpenTelemetryHandlerContext) describes the request being handled:

- [Transport](cref:Meziantou.Framework.OpenTelemetryCollector.OpenTelemetryHandlerContext.Transport): [Http](cref:Meziantou.Framework.OpenTelemetryCollector.OpenTelemetryTransport.Http) or [Grpc](cref:Meziantou.Framework.OpenTelemetryCollector.OpenTelemetryTransport.Grpc).
- [Method](cref:Meziantou.Framework.OpenTelemetryCollector.OpenTelemetryHandlerContext.Method): the HTTP method and path, such as `POST /v1/logs`, or the gRPC method, such as
  `/opentelemetry.proto.collector.logs.v1.LogsService/Export`.
- [PartialSuccess](cref:Meziantou.Framework.OpenTelemetryCollector.OpenTelemetryHandlerContext.PartialSuccess): reports the records the handler could not accept.

## Report rejected records

OTLP lets a receiver tell the client that it accepted only part of a request, in the `partial_success` field of the
response. Call `context.PartialSuccess.Reject(count, message)` for the records you could not accept:

<!-- ref:../../tests/Meziantou.Framework.OpenTelemetry.Tests/OpenTelemetryReceiverTests.cs;region=OpenTelemetryCollector_DropOversizedLogsHandler;format=md-fenced-code-block;dedent=true -->
```csharp
public sealed class DropOversizedLogsHandler : OpenTelemetryHandler
{
    private const int MaxLogRecordsPerRequest = 100;

    public override ValueTask HandleLogsAsync(OpenTelemetryHandlerContext context, ExportLogsServiceRequest request, CancellationToken cancellationToken)
    {
        var rejected = Store(request);
        if (rejected > 0)
        {
            context.PartialSuccess.Reject(rejected, "The storage quota is exceeded");
        }

        return ValueTask.CompletedTask;
    }

    public override ValueTask HandleTracesAsync(OpenTelemetryHandlerContext context, ExportTraceServiceRequest request, CancellationToken cancellationToken)
        => ValueTask.CompletedTask;

    public override ValueTask HandleMetricsAsync(OpenTelemetryHandlerContext context, ExportMetricsServiceRequest request, CancellationToken cancellationToken)
        => ValueTask.CompletedTask;

    // Stores the log records that fit in the quota, and returns the number of the other ones
    private static int Store(ExportLogsServiceRequest request)
    {
        var count = request.ResourceLogs.Sum(resourceLogs => resourceLogs.ScopeLogs.Sum(scopeLogs => scopeLogs.LogRecords.Count));
        return Math.Max(0, count - MaxLogRecordsPerRequest);
    }
}
```
<!-- endref -->

- The counts reported by the samplers and handlers of a request add up, and their messages are joined with `; `. The
  response reports them as rejected log records, spans or data points.
- `Reject(0, message)` sends a warning without rejecting any record.
- A call made after the response was sent is ignored. This happens for the traces that the
  [tail sampler](07-TailSampling.md) dispatches later.

## Unit test a handler

The constructors of [OpenTelemetryHandlerContext](cref:Meziantou.Framework.OpenTelemetryCollector.OpenTelemetryHandlerContext) and [OpenTelemetryPartialSuccess](cref:Meziantou.Framework.OpenTelemetryCollector.OpenTelemetryPartialSuccess) let a test call a handler directly,
without hosting the endpoints:

<!-- ref:../../tests/Meziantou.Framework.OpenTelemetry.Tests/OpenTelemetryReceiverTests.cs;region=OpenTelemetryCollector_UnitTestHandler;format=md-fenced-code-block;dedent=true -->
```csharp
var scopeLogs = new ScopeLogs();
for (var i = 0; i < 105; i++)
{
    scopeLogs.LogRecords.Add(new LogRecord());
}

var exportRequest = new ExportLogsServiceRequest { ResourceLogs = { new ResourceLogs { ScopeLogs = { scopeLogs } } } };

// Call the handler directly, without hosting the receiver
var partialSuccess = new OpenTelemetryPartialSuccess();
var context = new OpenTelemetryHandlerContext(OpenTelemetryTransport.Http, "POST /v1/logs", partialSuccess);
await new DropOversizedLogsHandler().HandleLogsAsync(context, exportRequest, CancellationToken.None);

Assert.Equal(5, partialSuccess.RejectedCount);
Assert.Equal("The storage quota is exceeded", partialSuccess.ErrorMessage);
```
<!-- endref -->

## Available fields

The OTLP messages are generated from a reduced copy of the OTLP Protobuf definitions. They have these fields only:

| Message | Fields |
| --- | --- |
| [ResourceLogs](cref:OpenTelemetry.Proto.Logs.V1.ResourceLogs), [ResourceSpans](cref:OpenTelemetry.Proto.Trace.V1.ResourceSpans), [ResourceMetrics](cref:OpenTelemetry.Proto.Metrics.V1.ResourceMetrics) | [Resource](cref:OpenTelemetry.Proto.Resource.V1.Resource), [ScopeLogs](cref:OpenTelemetry.Proto.Logs.V1.ScopeLogs) / [ScopeSpans](cref:OpenTelemetry.Proto.Trace.V1.ScopeSpans) / [ScopeMetrics](cref:OpenTelemetry.Proto.Metrics.V1.ScopeMetrics), `SchemaUrl` |
| [ScopeLogs](cref:OpenTelemetry.Proto.Logs.V1.ScopeLogs), [ScopeSpans](cref:OpenTelemetry.Proto.Trace.V1.ScopeSpans), [ScopeMetrics](cref:OpenTelemetry.Proto.Metrics.V1.ScopeMetrics) | `Scope`, [LogRecords](cref:OpenTelemetry.Proto.Logs.V1.ScopeLogs.LogRecords) / [Spans](cref:OpenTelemetry.Proto.Trace.V1.ScopeSpans.Spans) / [Metrics](cref:OpenTelemetry.Proto.Metrics.V1.ScopeMetrics.Metrics), `SchemaUrl` |
| [Resource](cref:OpenTelemetry.Proto.Resource.V1.Resource) | [Attributes](cref:OpenTelemetry.Proto.Resource.V1.Resource.Attributes) |
| [InstrumentationScope](cref:OpenTelemetry.Proto.Common.V1.InstrumentationScope) | [Name](cref:OpenTelemetry.Proto.Common.V1.InstrumentationScope.Name), [Version](cref:OpenTelemetry.Proto.Common.V1.InstrumentationScope.Version), [Attributes](cref:OpenTelemetry.Proto.Common.V1.InstrumentationScope.Attributes) |
| [LogRecord](cref:OpenTelemetry.Proto.Logs.V1.LogRecord) | [TimeUnixNano](cref:OpenTelemetry.Proto.Logs.V1.LogRecord.TimeUnixNano), [Body](cref:OpenTelemetry.Proto.Logs.V1.LogRecord.Body), [Attributes](cref:OpenTelemetry.Proto.Logs.V1.LogRecord.Attributes) |
| [Span](cref:OpenTelemetry.Proto.Trace.V1.Span) | [TraceId](cref:OpenTelemetry.Proto.Trace.V1.Span.TraceId), [SpanId](cref:OpenTelemetry.Proto.Trace.V1.Span.SpanId), [ParentSpanId](cref:OpenTelemetry.Proto.Trace.V1.Span.ParentSpanId), [Name](cref:OpenTelemetry.Proto.Trace.V1.Span.Name) |
| [Metric](cref:OpenTelemetry.Proto.Metrics.V1.Metric) | [Name](cref:OpenTelemetry.Proto.Metrics.V1.Metric.Name), [Description](cref:OpenTelemetry.Proto.Metrics.V1.Metric.Description), [Unit](cref:OpenTelemetry.Proto.Metrics.V1.Metric.Unit) |
| [KeyValue](cref:OpenTelemetry.Proto.Common.V1.KeyValue) | [Key](cref:OpenTelemetry.Proto.Common.V1.KeyValue.Key), [Value](cref:OpenTelemetry.Proto.Common.V1.KeyValue.Value) |
| [AnyValue](cref:OpenTelemetry.Proto.Common.V1.AnyValue) | [StringValue](cref:OpenTelemetry.Proto.Common.V1.AnyValue.StringValue), [BoolValue](cref:OpenTelemetry.Proto.Common.V1.AnyValue.BoolValue), [IntValue](cref:OpenTelemetry.Proto.Common.V1.AnyValue.IntValue), [DoubleValue](cref:OpenTelemetry.Proto.Common.V1.AnyValue.DoubleValue), [BytesValue](cref:OpenTelemetry.Proto.Common.V1.AnyValue.BytesValue) |

The other fields that a client sends, such as the severity of a log record, the times, kind, status and attributes of a
span, or the data points of a metric, are not available on these messages.
