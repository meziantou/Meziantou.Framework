using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;

namespace Meziantou.Framework.OpenTelemetryCollector.InMemory;

public sealed class InMemoryOpenTelemetryHandler(InMemoryOpenTelemetryHandlerOptions options) : OpenTelemetryHandler
{
    private readonly InMemoryOpenTelemetryItemCollection _logs = new(options.MaximumLogCount);
    private readonly InMemoryOpenTelemetryItemCollection _metrics = new(options.MaximumMetricCount);
    private readonly InMemoryOpenTelemetryItemCollection _traces = new(options.MaximumTraceCount);

    public IEnumerable<OpenTelemetryItem> Logs => _logs;
    public IEnumerable<OpenTelemetryItem> Traces => _traces;
    public IEnumerable<OpenTelemetryItem> Metrics => _metrics;

    public override ValueTask HandleLogsAsync(OpenTelemetryHandlerContext context, ExportLogsServiceRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _logs.Add(new OpenTelemetryLogsItem(request.Clone(), context.Method, DateTimeOffset.UtcNow));
        return ValueTask.CompletedTask;
    }

    public override ValueTask HandleTracesAsync(OpenTelemetryHandlerContext context, ExportTraceServiceRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _traces.Add(new OpenTelemetryTracesItem(request.Clone(), context.Method, DateTimeOffset.UtcNow));
        return ValueTask.CompletedTask;
    }

    public override ValueTask HandleMetricsAsync(OpenTelemetryHandlerContext context, ExportMetricsServiceRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _metrics.Add(new OpenTelemetryMetricsItem(request.Clone(), context.Method, DateTimeOffset.UtcNow));
        return ValueTask.CompletedTask;
    }
}
