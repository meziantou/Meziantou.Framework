using System.Text.Json.Serialization;

namespace Meziantou.Framework.ChromiumTracing;

public sealed class ChromiumTracingLinkIdEvent : ChromiumTracingEvent
{
    [JsonPropertyName("ph")]
    public override string Type => "=";

    [JsonPropertyName("id")]
    public string? Id { get; set; }
}
