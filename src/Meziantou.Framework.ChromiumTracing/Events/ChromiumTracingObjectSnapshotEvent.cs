using System.Text.Json.Serialization;

namespace Meziantou.Framework.ChromiumTracing;

/// <summary>Represents a snapshot of an object's state at a specific point in time.</summary>
public sealed class ChromiumTracingObjectSnapshotEvent : ChromiumTracingObjectEvent
{
    [JsonPropertyName("ph")]
    public override string Type => "O";
}
