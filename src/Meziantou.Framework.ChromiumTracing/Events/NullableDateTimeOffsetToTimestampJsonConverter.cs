using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meziantou.Framework.ChromiumTracing;

[SuppressMessage("Usage", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated dynamically")]
internal sealed class NullableDateTimeOffsetToTimestampJsonConverter : JsonConverter<DateTimeOffset?>
{
    internal const long TicksPerMicroseconds = 10;

    public override DateTimeOffset? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        var value = reader.GetInt64();
        return new DateTimeOffset(value * TicksPerMicroseconds, TimeSpan.Zero);
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset? value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteNumberValue(value.GetValueOrDefault().Ticks / TicksPerMicroseconds);
        }
    }
}
