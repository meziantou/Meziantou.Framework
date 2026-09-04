using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;

namespace Meziantou.Framework.OpenTelemetryCollector.InMemory;

public sealed class InMemoryOpenTelemetryHandler : OpenTelemetryHandler
{
    private readonly InMemoryOpenTelemetryItemCollection _logs;
    private readonly InMemoryOpenTelemetryItemCollection _metrics;
    private readonly InMemoryOpenTelemetryItemCollection _traces;
    private readonly TimeProvider _timeProvider;

    public InMemoryOpenTelemetryHandler(InMemoryOpenTelemetryHandlerOptions options)
        : this(options, TimeProvider.System)
    {
    }

    public InMemoryOpenTelemetryHandler(InMemoryOpenTelemetryHandlerOptions options, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _logs = new InMemoryOpenTelemetryItemCollection(options.MaximumLogCount);
        _metrics = new InMemoryOpenTelemetryItemCollection(options.MaximumMetricCount);
        _traces = new InMemoryOpenTelemetryItemCollection(options.MaximumTraceCount);
        _timeProvider = timeProvider;
    }

    public IEnumerable<OpenTelemetryItem> Logs => _logs;
    public IEnumerable<OpenTelemetryItem> Traces => _traces;
    public IEnumerable<OpenTelemetryItem> Metrics => _metrics;

    public override ValueTask HandleLogsAsync(OpenTelemetryHandlerContext context, ExportLogsServiceRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _logs.Add(new OpenTelemetryLogsItem(request.Clone(), context.Method, _timeProvider.GetUtcNow()));
        return ValueTask.CompletedTask;
    }

    public override ValueTask HandleTracesAsync(OpenTelemetryHandlerContext context, ExportTraceServiceRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _traces.Add(new OpenTelemetryTracesItem(request.Clone(), context.Method, _timeProvider.GetUtcNow()));
        return ValueTask.CompletedTask;
    }

    public override ValueTask HandleMetricsAsync(OpenTelemetryHandlerContext context, ExportMetricsServiceRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _metrics.Add(new OpenTelemetryMetricsItem(request.Clone(), context.Method, _timeProvider.GetUtcNow()));
        return ValueTask.CompletedTask;
    }
}
