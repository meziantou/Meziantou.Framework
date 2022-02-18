using System.Text.Json.Serialization;

namespace Meziantou.Framework.ChromiumTracing;

public sealed class ChromiumTracingObjectDestroyedEvent : ChromiumTracingObjectEvent
{
    [JsonPropertyName("ph")]
    public override string Type => "D";
}
