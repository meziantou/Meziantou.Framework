using System.Text.Json.Serialization;

namespace Meziantou.Framework.ChromiumTracing;

/// <summary>Represents a clock synchronization event used to align timestamps across different trace sources.</summary>
public sealed class ChromiumTracingClockSyncEvent : ChromiumTracingEvent
{
    [JsonPropertyName("ph")]
    public override string Type => "c";

    /// <summary>Gets the event name, which is always "clock_sync". Setting it to <see langword="null"/> or to "clock_sync" is a no-op; any other value is rejected.</summary>
    [JsonPropertyName("name")]
    public override string? Name
    {
        get => "clock_sync";
        set
        {
            if (value is not null && !string.Equals(value, "clock_sync", StringComparison.Ordinal))
                throw new ArgumentException("The name of a clock sync event is always 'clock_sync'", nameof(value));
        }
    }
}
