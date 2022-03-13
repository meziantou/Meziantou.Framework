using System.Text.Json.Serialization;

namespace Meziantou.Framework.ChromiumTracing;

public abstract class ChromiumTracingEvent
{
    // Note: overriden members should also set the attribute (https://github.com/dotnet/runtime/issues/50078)
    [JsonPropertyName("ph")]
    public abstract string Type { get; }

    [JsonPropertyName("name")]
    public virtual string? Name { get; set; }

    [JsonPropertyName("cat")]
    public string? Category { get; set; }

    [JsonPropertyName("ts")]
    [JsonConverter(typeof(DateTimeOffsetToTimestampJsonConverter))]
    public DateTimeOffset Timestamp { get; set; }

    [JsonPropertyName("tts")]
    [JsonConverter(typeof(NullableDateTimeOffsetToTimestampJsonConverter))]
    public DateTimeOffset? ThreadTimestamp { get; set; }

    [JsonPropertyName("pid")]
    public int? ProcessId { get; set; }

    [JsonPropertyName("tid")]
    public int? ThreadId { get; set; }

    [JsonPropertyName("cname")]
    public string? ColorName { get; set; }

    [JsonPropertyName("stack")]
    public IEnumerable<string>? StackTrace { get; set; }

    [JsonPropertyName("args")]
    public IReadOnlyDictionary<string, object?>? Arguments { get; set; }
}
