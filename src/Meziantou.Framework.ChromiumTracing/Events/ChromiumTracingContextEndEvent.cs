using System.Text.Json.Serialization;

namespace Meziantou.Framework.ChromiumTracing;

/// <summary>Represents the end of a context scope. Must be paired with a <see cref="ChromiumTracingContextBeginEvent"/>.</summary>
public sealed class ChromiumTracingContextEndEvent : ChromiumTracingContextEvent
{
    [JsonPropertyName("ph")]
    public override string Type => ")";
}
