using System.Text.Json.Serialization;

namespace Meziantou.Framework.ChromiumTracing;

public sealed class ChromiumTracingMarkEvent : ChromiumTracingEvent
{
    [JsonPropertyName("ph")]
    public override string Type => "R";
}
