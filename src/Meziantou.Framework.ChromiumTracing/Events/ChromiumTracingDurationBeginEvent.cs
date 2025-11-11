using System.Text.Json.Serialization;

namespace Meziantou.Framework.ChromiumTracing;

/// <summary>Represents the beginning of a duration event. Must be paired with a <see cref="ChromiumTracingDurationEndEvent"/>.</summary>
/// <example>
/// <code>
/// await writer.WriteEventAsync(new ChromiumTracingDurationBeginEvent
/// {
///     Name = "Processing",
///     Category = "compute",
///     Timestamp = DateTimeOffset.UtcNow,
///     ProcessId = Environment.ProcessId,
///     ThreadId = Environment.CurrentManagedThreadId
/// });
/// </code>
/// </example>
public sealed class ChromiumTracingDurationBeginEvent : ChromiumTracingDurationEvent
{
    [JsonPropertyName("ph")]
    public override string Type => "B";
}
