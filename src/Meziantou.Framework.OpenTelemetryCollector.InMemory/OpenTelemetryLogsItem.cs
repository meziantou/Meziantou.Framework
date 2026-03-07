using OpenTelemetry.Proto.Collector.Logs.V1;

namespace Meziantou.Framework.OpenTelemetryCollector.InMemory;

public sealed class OpenTelemetryLogsItem : OpenTelemetryItem
{
    internal OpenTelemetryLogsItem(ExportLogsServiceRequest request, string method, DateTimeOffset receivedAt)
        : base(OpenTelemetryItemType.Logs, method, receivedAt)
    {
        Request = request;
    }

    public ExportLogsServiceRequest Request { get; }
}
