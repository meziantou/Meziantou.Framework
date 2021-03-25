using System.Text.Json.Serialization;

namespace Meziantou.Framework.ChromiumTracing
{
    public sealed class ChromiumTracingAsyncBeginEvent : ChromiumTracingAsyncEvent
    {
        [JsonPropertyName("ph")]
        public override string Type => "b";
    }
}
