using System.Text.Json.Serialization;

namespace Meziantou.Framework.ChromiumTracing;

public sealed class ChromiumTracingMemoryDumpGlobalEvent : ChromiumTracingMemoryDumpEvent
{
    [JsonPropertyName("ph")]
    public override string Type => "V";
}
