using System.Text.Json.Serialization;

namespace Meziantou.Framework.ChromiumTracing;

public sealed class ChromiumTracingDurationBeginEvent : ChromiumTracingDurationEvent
{
    [JsonPropertyName("ph")]
    public override string Type => "B";
}
