using System.Text.Json.Serialization;

namespace Meziantou.Framework.ChromiumTracing;

/// <summary>Represents a step in a flow event chain.</summary>
public sealed class ChromiumTracingFlowStepEvent : ChromiumTracingFlowEvent
{
    [JsonPropertyName("ph")]
    public override string Type => "t";
}
