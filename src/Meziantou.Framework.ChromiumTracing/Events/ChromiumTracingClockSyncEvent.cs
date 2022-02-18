using System.Text.Json.Serialization;

namespace Meziantou.Framework.ChromiumTracing;

public sealed class ChromiumTracingClockSyncEvent : ChromiumTracingEvent
{
    [JsonPropertyName("ph")]
    public override string Type => "c";

    [JsonPropertyName("name")]
    public override string? Name
    {
        get => "clock_sync";
        set
        {
            if (value != "clock_sync")
                throw new InvalidOperationException("Name is not settable for a Clock Sync event");
        }
    }
}
