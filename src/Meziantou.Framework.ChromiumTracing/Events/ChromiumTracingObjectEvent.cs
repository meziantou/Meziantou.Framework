using System.Text.Json.Serialization;

namespace Meziantou.Framework.ChromiumTracing;

/// <summary>Base class for object lifecycle events.</summary>
public abstract class ChromiumTracingObjectEvent : ChromiumTracingEvent
{
    /// <summary>Gets or sets the unique identifier for the object.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }
}
