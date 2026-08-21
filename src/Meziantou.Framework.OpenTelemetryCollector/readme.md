## Meziantou.Framework.OpenTelemetryCollector

This package provides OpenTelemetry OTLP receiver endpoints for:

- HTTP (`/v1/logs`, `/v1/traces`, `/v1/metrics`), using either `application/x-protobuf` or `application/json` (OTLP/JSON)
- gRPC (`LogsService`, `TraceService`, `MetricsService`)

OTLP/HTTP requests compressed with `Content-Encoding: gzip` are supported, and responses use the same encoding as the request.

The receiver API is abstract, so you can implement custom handling logic and register one or multiple receivers.

For an in-memory implementation, use the `Meziantou.Framework.OpenTelemetryCollector.InMemory` package.

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenTelemetryReceiver<MyReceiver>();

var app = builder.Build();
app.MapOpenTelemetryReceiverEndpoints();

app.Run();
```

### Request sampling

Use request samplers to drop logs, traces, or metrics before they are dispatched to handlers:

```csharp
public sealed class KeepOnlyNonEmptyRequestsSampler : OpenTelemetrySampler
{
    public override ValueTask<bool> ShouldSampleLogsAsync(OpenTelemetryHandlerContext context, ExportLogsServiceRequest request, CancellationToken cancellationToken)
        => ValueTask.FromResult(request.ResourceLogs.Count > 0);

    public override ValueTask<bool> ShouldSampleTracesAsync(OpenTelemetryHandlerContext context, ExportTraceServiceRequest request, CancellationToken cancellationToken)
        => ValueTask.FromResult(request.ResourceSpans.Count > 0);

    public override ValueTask<bool> ShouldSampleMetricsAsync(OpenTelemetryHandlerContext context, ExportMetricsServiceRequest request, CancellationToken cancellationToken)
        => ValueTask.FromResult(request.ResourceMetrics.Count > 0);
}

builder.Services.AddOpenTelemetryReceiver<MyReceiver>(options =>
{
    options.Samplers.Add(new KeepOnlyNonEmptyRequestsSampler());
});
```

### Trace tail filtering

Use tail sampling for traces when child spans can arrive before the root span. The collector buffers spans per trace id and evaluates the sampler when:

- the root span is observed
- or `MaxTraceDuration` is reached

```csharp
builder.Services.AddOpenTelemetryReceiver<MyReceiver>(options =>
{
    options.Samplers.Add(new OpenTelemetryTailSampler
    {
        MaxTraceDuration = TimeSpan.FromSeconds(30),
        MaxBufferedSpansPerTrace = 5000,
        MaxBufferedSpans = 100_000,
        OverflowPolicy = OpenTelemetryTailBufferOverflowPolicy.DropWholeTrace,
        ShouldSample = static (context, _) =>
            ValueTask.FromResult(context.RootSpan?.Name?.Contains("critical", StringComparison.OrdinalIgnoreCase) is true),
    });
});
```

Traces that never receive their root span are evaluated by a background sweep, so they are released even when no other
trace is received. The sweep runs every `SweepInterval`, which defaults to a quarter of `MaxTraceDuration`.

Once spans left the buffer they are dispatched to the handlers without a cancellation token: the originating request has
already been answered, so aborting the dispatch would silently discard buffered data belonging to other requests.

Overflow behavior is configurable through `OpenTelemetryTailBufferOverflowPolicy`:

- `DropWholeTrace`: drop the whole buffered trace
- `DropOldestSpans`: keep newest spans
- `DropNewestSpans`: keep oldest spans

A trace that exceeds `MaxBufferedSpansPerTrace` while using `DropWholeTrace` is remembered, so the spans of the same
trace received later are dropped too instead of being emitted as fragments. Exceeding the global `MaxBufferedSpans`
limit is transient back pressure and does not mark the trace as dropped.

### Reporting rejected records

Samplers and handlers can report the records they could not accept. They are sent back to the client in the OTLP
`partial_success` response field:

```csharp
public sealed class DropOversizedLogsHandler : OpenTelemetryHandler
{
    public override ValueTask HandleLogsAsync(OpenTelemetryHandlerContext context, ExportLogsServiceRequest request, CancellationToken cancellationToken)
    {
        var rejected = Store(request);
        if (rejected > 0)
        {
            context.PartialSuccess.Reject(rejected, "The storage quota is exceeded");
        }

        return ValueTask.CompletedTask;
    }
}
```

Spans dropped by `OpenTelemetryTailSampler` because a buffer limit is reached are reported the same way.

### Limiting the request size

OTLP/HTTP payloads larger than `MaxHttpRequestBodySize` (20 MiB by default) are rejected with `413 Content Too Large`.
The limit applies to the payload after decompression, so a compressed request cannot expand beyond it.

```csharp
builder.Services.AddOpenTelemetryReceiver<MyReceiver>(options =>
{
    options.MaxHttpRequestBodySize = 4 * 1024 * 1024;
});
```
