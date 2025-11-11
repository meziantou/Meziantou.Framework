using System.Text.Json.Serialization;

namespace Meziantou.Framework.ChromiumTracing;

/// <summary>Represents the beginning of a context scope. Must be paired with a <see cref="ChromiumTracingContextEndEvent"/>.</summary>
public sealed class ChromiumTracingContextBeginEvent : ChromiumTracingContextEvent
{
    [JsonPropertyName("ph")]
    public override string Type => "(";
}
