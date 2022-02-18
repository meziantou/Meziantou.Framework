using System.Text.Json.Serialization;

namespace Meziantou.Framework.ChromiumTracing;

public sealed class ChromiumTracingContextEndEvent : ChromiumTracingContextEvent
{
    [JsonPropertyName("ph")]
    public override string Type => ")";
}
