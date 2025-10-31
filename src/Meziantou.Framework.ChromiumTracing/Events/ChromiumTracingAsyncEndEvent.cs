using System.Text.Json.Serialization;

namespace Meziantou.Framework.ChromiumTracing;

/// <summary>Represents the end of an asynchronous operation. Must be paired with a <see cref="ChromiumTracingAsyncBeginEvent"/> with the same Id.</summary>
public sealed class ChromiumTracingAsyncEndEvent : ChromiumTracingAsyncEvent
{
    [JsonPropertyName("ph")]
    public override string Type => "e";
}
