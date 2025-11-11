using System.Text.Json.Serialization;

namespace Meziantou.Framework.ChromiumTracing;

/// <summary>Represents an object destruction event.</summary>
public sealed class ChromiumTracingObjectDestroyedEvent : ChromiumTracingObjectEvent
{
    [JsonPropertyName("ph")]
    public override string Type => "D";
}
