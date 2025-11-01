using System.Text.Json.Serialization;

namespace Meziantou.Framework.ChromiumTracing;

/// <summary>Represents a mark event used to highlight a point in the timeline.</summary>
public sealed class ChromiumTracingMarkEvent : ChromiumTracingEvent
{
    [JsonPropertyName("ph")]
    public override string Type => "R";
}
