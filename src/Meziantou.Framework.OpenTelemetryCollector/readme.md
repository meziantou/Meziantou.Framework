## Meziantou.Framework.OpenTelemetryCollector

This package provides OpenTelemetry OTLP receiver endpoints for:

- HTTP (`/v1/logs`, `/v1/traces`, `/v1/metrics`)
- gRPC (`LogsService`, `TraceService`, `MetricsService`)

The receiver API is abstract, so you can implement custom handling logic and register one or multiple receivers.

For an in-memory implementation, use the `Meziantou.Framework.OpenTelemetryCollector.InMemory` package.

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenTelemetryReceiver<MyReceiver>();

var app = builder.Build();
app.MapOpenTelemetryReceiverEndpoints();

app.Run();
```

### Request filtering

Use request filters to drop logs, traces, or metrics before they are dispatched to handlers:

```csharp
public sealed class KeepOnlyNonEmptyRequestsFilter : OpenTelemetryFilter
{
    public override ValueTask<bool> ShouldProcessLogsAsync(OpenTelemetryHandlerContext context, ExportLogsServiceRequest request, CancellationToken cancellationToken)
        => ValueTask.FromResult(request.ResourceLogs.Count > 0);

    public override ValueTask<bool> ShouldProcessTracesAsync(OpenTelemetryHandlerContext context, ExportTraceServiceRequest request, CancellationToken cancellationToken)
        => ValueTask.FromResult(request.ResourceSpans.Count > 0);

    public override ValueTask<bool> ShouldProcessMetricsAsync(OpenTelemetryHandlerContext context, ExportMetricsServiceRequest request, CancellationToken cancellationToken)
        => ValueTask.FromResult(request.ResourceMetrics.Count > 0);
}

builder.Services.AddOpenTelemetryReceiver<MyReceiver>(options =>
{
    options.Filters.Add(new KeepOnlyNonEmptyRequestsFilter());
});
```

### Trace tail filtering

Use tail filtering for traces when child spans can arrive before the root span. The collector buffers spans per trace id and evaluates the filter when:

- the root span is observed
- or `MaxTraceDuration` is reached

```csharp
builder.Services.AddOpenTelemetryReceiver<MyReceiver>(options =>
{
    options.Filters.Add(new OpenTelemetryTailSamplingFilter
    {
        MaxTraceDuration = TimeSpan.FromSeconds(30),
        MaxBufferedSpansPerTrace = 5000,
        MaxBufferedSpans = 100_000,
        OverflowPolicy = OpenTelemetryTailBufferOverflowPolicy.DropWholeTrace,
        Filter = static (context, _) =>
            ValueTask.FromResult(context.RootSpan?.Name?.Contains("critical", StringComparison.OrdinalIgnoreCase) is true),
    });
});
```

Overflow behavior is configurable through `OpenTelemetryTailBufferOverflowPolicy`:

- `DropWholeTrace`: drop the whole buffered trace
- `DropOldestSpans`: keep newest spans
- `DropNewestSpans`: keep oldest spans
