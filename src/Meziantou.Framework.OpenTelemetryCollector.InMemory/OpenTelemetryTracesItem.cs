using OpenTelemetry.Proto.Collector.Trace.V1;

namespace Meziantou.Framework.OpenTelemetryCollector.InMemory;

public sealed class OpenTelemetryTracesItem : OpenTelemetryItem
{
    internal OpenTelemetryTracesItem(ExportTraceServiceRequest request, string method, DateTimeOffset receivedAt)
        : base(OpenTelemetryItemType.Traces, method, receivedAt)
    {
        Request = request;
    }

    public ExportTraceServiceRequest Request { get; }
}
