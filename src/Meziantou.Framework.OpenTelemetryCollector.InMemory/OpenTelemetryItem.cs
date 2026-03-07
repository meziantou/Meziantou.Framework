namespace Meziantou.Framework.OpenTelemetryCollector.InMemory;

public abstract class OpenTelemetryItem
{
    private protected OpenTelemetryItem(OpenTelemetryItemType itemType, string method, DateTimeOffset receivedAt)
    {
        ItemType = itemType;
        Method = method;
        ReceivedAt = receivedAt;
    }

    public OpenTelemetryItemType ItemType { get; }

    public string Method { get; }

    public DateTimeOffset ReceivedAt { get; }
}
