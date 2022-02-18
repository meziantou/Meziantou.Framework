using System.Text.Json.Serialization;

namespace Meziantou.Framework.ChromiumTracing;

public abstract class ChromiumTracingObjectEvent : ChromiumTracingEvent
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }
}
