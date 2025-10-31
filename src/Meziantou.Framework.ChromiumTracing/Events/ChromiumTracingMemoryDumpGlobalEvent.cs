using System.Text.Json.Serialization;

namespace Meziantou.Framework.ChromiumTracing;

/// <summary>Represents a global memory dump event.</summary>
public sealed class ChromiumTracingMemoryDumpGlobalEvent : ChromiumTracingMemoryDumpEvent
{
    [JsonPropertyName("ph")]
    public override string Type => "V";
}
