using System.Text.Json.Serialization;

namespace Meziantou.Framework.ChromiumTracing;

/// <summary>Represents the end of a flow event.</summary>
public sealed class ChromiumTracingFlowEndEvent : ChromiumTracingFlowEvent
{
    [JsonPropertyName("ph")]
    public override string Type => "f";

    /// <summary>Gets or sets the binding point that specifies how the flow event connects to other events.</summary>
    [JsonPropertyName("bp")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    [JsonConverter(typeof(BindingPointJsonConverter))]
    public BindingPoint BindingPoint { get; set; }
}
