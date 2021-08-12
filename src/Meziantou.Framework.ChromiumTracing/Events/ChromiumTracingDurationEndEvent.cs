using System.Text.Json.Serialization;

namespace Meziantou.Framework.ChromiumTracing;

public sealed class ChromiumTracingDurationEndEvent : ChromiumTracingDurationEvent
{
    [JsonPropertyName("ph")]
    public override string Type => "E";
}
