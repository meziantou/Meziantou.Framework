using System.Text.Json.Serialization;

namespace Meziantou.Framework.ChromiumTracing;

public sealed class ChromiumTracingMemoryDumpProcessEvent : ChromiumTracingMemoryDumpEvent
{
    [JsonPropertyName("ph")]
    public override string Type => "v";
}
