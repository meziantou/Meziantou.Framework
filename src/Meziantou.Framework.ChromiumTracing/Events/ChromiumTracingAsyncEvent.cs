using System.Text.Json.Serialization;

namespace Meziantou.Framework.ChromiumTracing
{
    public abstract class ChromiumTracingAsyncEvent : ChromiumTracingEvent
    {
        [JsonPropertyName("id")]
        public int? Id { get; set; }
    }
}
