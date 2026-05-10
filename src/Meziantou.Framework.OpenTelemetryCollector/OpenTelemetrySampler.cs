using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;

namespace Meziantou.Framework.OpenTelemetryCollector;

public abstract class OpenTelemetrySampler
{
    public virtual ValueTask<bool> ShouldSampleLogsAsync(OpenTelemetryHandlerContext context, ExportLogsServiceRequest request, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(true);
    }

    public virtual ValueTask<bool> ShouldSampleTracesAsync(OpenTelemetryHandlerContext context, ExportTraceServiceRequest request, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(true);
    }

    public virtual ValueTask<bool> ShouldSampleMetricsAsync(OpenTelemetryHandlerContext context, ExportMetricsServiceRequest request, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(true);
    }
}
