using System.Text.Json.Serialization;

namespace Meziantou.Framework.ChromiumTracing;

/// <summary>Represents an instant event within an asynchronous operation.</summary>
public sealed class ChromiumTracingAsyncInstantEvent : ChromiumTracingAsyncEvent
{
    [JsonPropertyName("ph")]
    public override string Type => "n";
}
