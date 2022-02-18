using System.Text.Json.Serialization;

namespace Meziantou.Framework.ChromiumTracing;

public sealed class ChromiumTracingObjectCreatedEvent : ChromiumTracingObjectEvent
{
    [JsonPropertyName("ph")]
    public override string Type => "N";
}
