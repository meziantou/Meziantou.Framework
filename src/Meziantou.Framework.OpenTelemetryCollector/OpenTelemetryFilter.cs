using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;

namespace Meziantou.Framework.OpenTelemetryCollector;

public abstract class OpenTelemetryFilter
{
    public virtual ValueTask<bool> ShouldProcessLogsAsync(OpenTelemetryHandlerContext context, ExportLogsServiceRequest request, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(true);
    }

    public virtual ValueTask<bool> ShouldProcessTracesAsync(OpenTelemetryHandlerContext context, ExportTraceServiceRequest request, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(true);
    }

    public virtual ValueTask<bool> ShouldProcessMetricsAsync(OpenTelemetryHandlerContext context, ExportMetricsServiceRequest request, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(true);
    }
}
