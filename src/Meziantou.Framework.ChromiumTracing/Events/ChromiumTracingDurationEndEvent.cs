using System.Text.Json.Serialization;

namespace Meziantou.Framework.ChromiumTracing;

/// <summary>Represents the end of a duration event. Must be paired with a <see cref="ChromiumTracingDurationBeginEvent"/> with the same name.</summary>
public sealed class ChromiumTracingDurationEndEvent : ChromiumTracingDurationEvent
{
    [JsonPropertyName("ph")]
    public override string Type => "E";
}
