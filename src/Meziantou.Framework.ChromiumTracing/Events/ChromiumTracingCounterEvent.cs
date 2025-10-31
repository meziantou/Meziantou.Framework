using System.Text.Json.Serialization;

namespace Meziantou.Framework.ChromiumTracing;

/// <summary>Represents a counter event that displays counter values over time.</summary>
/// <example>
/// <code>
/// await writer.WriteEventAsync(new ChromiumTracingCounterEvent
/// {
///     Name = "Memory Usage",
///     Category = "system",
///     Timestamp = DateTimeOffset.UtcNow,
///     ProcessId = Environment.ProcessId,
///     Arguments = new Dictionary&lt;string, object?&gt;
///     {
///         ["bytes"] = 1024 * 1024 * 500
///     }
/// });
/// </code>
/// </example>
public sealed class ChromiumTracingCounterEvent : ChromiumTracingEvent
{
    [JsonPropertyName("ph")]
    public override string Type => "C";

    /// <summary>Gets or sets the unique identifier for the counter series.</summary>
    [JsonPropertyName("id")]
    public int? Id { get; set; }
}
