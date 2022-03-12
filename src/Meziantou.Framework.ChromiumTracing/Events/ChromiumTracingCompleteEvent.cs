using System.Text.Json.Serialization;

namespace Meziantou.Framework.ChromiumTracing;

public sealed class ChromiumTracingCompleteEvent : ChromiumTracingEvent
{
    [JsonPropertyName("ph")]
    public override string Type => "X";

    [JsonPropertyName("dur")]
    [JsonConverter(typeof(TimeSpanToTimestampJsonConverter))]
    public TimeSpan Duration { get; set; }

    [JsonPropertyName("tdur")]
    [JsonConverter(typeof(NullableTimeSpanToTimestampJsonConverter))]
    public TimeSpan? ThreadDuration { get; set; }

    [JsonPropertyName("estack")]
    public IEnumerable<string>? EndStackTrace { get; set; }
}
