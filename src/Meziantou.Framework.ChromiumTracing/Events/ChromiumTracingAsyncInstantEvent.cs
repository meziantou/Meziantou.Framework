using System.Text.Json.Serialization;

namespace Meziantou.Framework.ChromiumTracing;

public sealed class ChromiumTracingAsyncInstantEvent : ChromiumTracingAsyncEvent
{
    [JsonPropertyName("ph")]
    public override string Type => "n";
}
