using System.Text.Json.Serialization;

namespace Meziantou.Framework.ChromiumTracing;

public sealed class ChromiumTracingContextBeginEvent : ChromiumTracingContextEvent
{
    [JsonPropertyName("ph")]
    public override string Type => "(";
}
