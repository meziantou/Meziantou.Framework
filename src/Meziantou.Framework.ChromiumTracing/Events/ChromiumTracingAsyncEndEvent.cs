using System.Text.Json.Serialization;

namespace Meziantou.Framework.ChromiumTracing;

public sealed class ChromiumTracingAsyncEndEvent : ChromiumTracingAsyncEvent
{
    [JsonPropertyName("ph")]
    public override string Type => "e";
}
