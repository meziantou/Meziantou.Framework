using System.Text.Json.Serialization;

namespace Meziantou.Framework.ChromiumTracing
{
    public sealed class ChromiumTracingFlowBeginEvent : ChromiumTracingFlowEvent
    {
        [JsonPropertyName("ph")]
        public override string Type => "s";
    }
}
