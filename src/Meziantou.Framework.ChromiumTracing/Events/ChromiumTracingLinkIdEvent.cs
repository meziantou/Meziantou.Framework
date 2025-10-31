using System.Text.Json.Serialization;

namespace Meziantou.Framework.ChromiumTracing;

/// <summary>Represents a link ID event that associates events with a unique identifier.</summary>
public sealed class ChromiumTracingLinkIdEvent : ChromiumTracingEvent
{
    [JsonPropertyName("ph")]
    public override string Type => "=";

    /// <summary>Gets or sets the unique identifier for linking events.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }
}
