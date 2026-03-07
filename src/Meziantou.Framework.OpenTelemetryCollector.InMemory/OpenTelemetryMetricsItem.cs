using OpenTelemetry.Proto.Collector.Metrics.V1;

namespace Meziantou.Framework.OpenTelemetryCollector.InMemory;

public sealed class OpenTelemetryMetricsItem : OpenTelemetryItem
{
    internal OpenTelemetryMetricsItem(ExportMetricsServiceRequest request, string method, DateTimeOffset receivedAt)
        : base(OpenTelemetryItemType.Metrics, method, receivedAt)
    {
        Request = request;
    }

    public ExportMetricsServiceRequest Request { get; }
}
