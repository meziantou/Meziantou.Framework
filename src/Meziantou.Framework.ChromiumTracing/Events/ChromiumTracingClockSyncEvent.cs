using System.Text.Json.Serialization;

namespace Meziantou.Framework.ChromiumTracing;

/// <summary>Represents a clock synchronization event used to align timestamps across different trace sources.</summary>
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
