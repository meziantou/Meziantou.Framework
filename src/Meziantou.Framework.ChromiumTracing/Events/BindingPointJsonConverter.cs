using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meziantou.Framework.ChromiumTracing
{
    [SuppressMessage("Usage", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated dynamically")]
    internal sealed class BindingPointJsonConverter : JsonConverter<BindingPoint>
    {
        public override BindingPoint Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            throw new NotSupportedException();
        }

        public override void Write(Utf8JsonWriter writer, BindingPoint value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value switch
            {
                BindingPoint.EnclosingSlice => "e",
                _ => throw new ArgumentException($"Value '{value}' is invalid", nameof(value)),
            });
        }
    }
}
