using System.Text.Json.Serialization;

namespace Meziantou.Framework.ChromiumTracing;

/// <summary>Base class for all Chromium trace events.</summary>
public abstract class ChromiumTracingEvent
{
    /// <summary>Gets the event type identifier (phase) used in the trace format.</summary>
    // Note: overriden members should also set the attribute (https://github.com/dotnet/runtime/issues/50078)
    [JsonPropertyName("ph")]
    public abstract string Type { get; }

    /// <summary>Gets or sets the name of the event.</summary>
    [JsonPropertyName("name")]
    public virtual string? Name { get; set; }

    /// <summary>Gets or sets the category of the event. Multiple categories can be specified by separating them with commas.</summary>
    [JsonPropertyName("cat")]
    public string? Category { get; set; }

    /// <summary>Gets or sets the timestamp when the event occurred.</summary>
    [JsonPropertyName("ts")]
    [JsonConverter(typeof(DateTimeOffsetToTimestampJsonConverter))]
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>Gets or sets the thread clock timestamp when the event occurred.</summary>
    [JsonPropertyName("tts")]
    [JsonConverter(typeof(NullableDateTimeOffsetToTimestampJsonConverter))]
    public DateTimeOffset? ThreadTimestamp { get; set; }

    /// <summary>Gets or sets the process ID that emitted the event.</summary>
    [JsonPropertyName("pid")]
    public int? ProcessId { get; set; }

    /// <summary>Gets or sets the thread ID that emitted the event.</summary>
    [JsonPropertyName("tid")]
    public int? ThreadId { get; set; }

    /// <summary>Gets or sets the color name for the event display in the trace viewer.</summary>
    [JsonPropertyName("cname")]
    public string? ColorName { get; set; }

    /// <summary>Gets or sets the stack trace at the time the event was recorded.</summary>
    [JsonPropertyName("stack")]
    public IEnumerable<string>? StackTrace { get; set; }

    /// <summary>Gets or sets additional arguments or metadata associated with the event.</summary>
    [JsonPropertyName("args")]
    public IReadOnlyDictionary<string, object?>? Arguments { get; set; }
}
