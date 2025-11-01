using System.Text.Json.Serialization;

namespace Meziantou.Framework.ChromiumTracing;

/// <summary>Base class for asynchronous events that track operations across multiple threads.</summary>
public abstract class ChromiumTracingAsyncEvent : ChromiumTracingEvent
{
    /// <summary>Gets or sets the unique identifier for the asynchronous operation.</summary>
    [JsonPropertyName("id")]
    public int? Id { get; set; }
}
