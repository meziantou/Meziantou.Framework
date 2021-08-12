using System.Text.Json.Serialization;

namespace Meziantou.Framework.ChromiumTracing;

public sealed class ChromiumTracingFlowStepEvent : ChromiumTracingFlowEvent
{
    [JsonPropertyName("ph")]
    public override string Type => "t";
}
