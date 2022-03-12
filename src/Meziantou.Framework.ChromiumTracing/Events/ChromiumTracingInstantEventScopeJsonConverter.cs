using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meziantou.Framework.ChromiumTracing;

[SuppressMessage("Usage", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated dynamically")]
internal sealed class ChromiumTracingInstantEventScopeJsonConverter : JsonConverter<ChromiumTracingInstantEventScope?>
{
    public override ChromiumTracingInstantEventScope? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        throw new NotSupportedException();
    }

    public override void Write(Utf8JsonWriter writer, ChromiumTracingInstantEventScope? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteStringValue(value switch
            {
                ChromiumTracingInstantEventScope.Global => "g",
                ChromiumTracingInstantEventScope.Process => "p",
                ChromiumTracingInstantEventScope.Thread => "t",
                _ => throw new ArgumentException($"Value '{value}' is invalid", nameof(value)),
            });
        }
    }
}
