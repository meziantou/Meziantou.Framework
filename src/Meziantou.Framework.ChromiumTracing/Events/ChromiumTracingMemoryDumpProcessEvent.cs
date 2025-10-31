using System.Text.Json.Serialization;

namespace Meziantou.Framework.ChromiumTracing;

/// <summary>Represents a process-level memory dump event.</summary>
public sealed class ChromiumTracingMemoryDumpProcessEvent : ChromiumTracingMemoryDumpEvent
{
    [JsonPropertyName("ph")]
    public override string Type => "v";
}
