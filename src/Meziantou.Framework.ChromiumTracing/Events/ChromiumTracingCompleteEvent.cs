using System.Text.Json.Serialization;

namespace Meziantou.Framework.ChromiumTracing;

/// <summary>Represents a complete event that tracks an operation with a start time and duration in a single event.</summary>
/// <example>
/// <code>
/// await writer.WriteEventAsync(new ChromiumTracingCompleteEvent
/// {
///     Name = "Processing",
///     Category = "work",
///     Timestamp = startTime,
///     Duration = TimeSpan.FromMilliseconds(200),
///     ProcessId = Environment.ProcessId,
///     ThreadId = Environment.CurrentManagedThreadId
/// });
/// </code>
/// </example>
public sealed class ChromiumTracingCompleteEvent : ChromiumTracingEvent
{
    [JsonPropertyName("ph")]
    public override string Type => "X";

    /// <summary>Gets or sets the duration of the event.</summary>
    [JsonPropertyName("dur")]
    [JsonConverter(typeof(TimeSpanToTimestampJsonConverter))]
    public TimeSpan Duration { get; set; }

    /// <summary>Gets or sets the thread clock duration of the event.</summary>
    [JsonPropertyName("tdur")]
    [JsonConverter(typeof(NullableTimeSpanToTimestampJsonConverter))]
    public TimeSpan? ThreadDuration { get; set; }

    /// <summary>Gets or sets the stack trace at the end of the event.</summary>
    [JsonPropertyName("estack")]
    public IEnumerable<string>? EndStackTrace { get; set; }
}
