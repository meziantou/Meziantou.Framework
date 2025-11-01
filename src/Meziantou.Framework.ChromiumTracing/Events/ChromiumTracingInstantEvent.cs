using System.Text.Json.Serialization;

namespace Meziantou.Framework.ChromiumTracing;

/// <summary>Represents an instant event that marks a single point in time.</summary>
/// <example>
/// <code>
/// await writer.WriteEventAsync(new ChromiumTracingInstantEvent
/// {
///     Name = "Checkpoint",
///     Category = "milestone",
///     Timestamp = DateTimeOffset.UtcNow,
///     Scope = ChromiumTracingInstantEventScope.Thread,
///     ProcessId = Environment.ProcessId,
///     ThreadId = Environment.CurrentManagedThreadId
/// });
/// </code>
/// </example>
public sealed class ChromiumTracingInstantEvent : ChromiumTracingEvent
{
    [JsonPropertyName("ph")]
    public override string Type => "i";

    /// <summary>Gets or sets the scope of the instant event (Global, Process, or Thread).</summary>
    [JsonPropertyName("s")]
    [JsonConverter(typeof(ChromiumTracingInstantEventScopeJsonConverter))]
    public ChromiumTracingInstantEventScope? Scope { get; set; }
}
