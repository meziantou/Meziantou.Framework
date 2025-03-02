using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meziantou.Framework.ChromiumTracing;

internal sealed class NullableTimeSpanToTimestampJsonConverter : JsonConverter<TimeSpan?>
{
    internal const long TicksPerMicroseconds = 10;

    public override TimeSpan? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetInt64();
        return new TimeSpan(value * TicksPerMicroseconds);
    }

    public override void Write(Utf8JsonWriter writer, TimeSpan? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteNumberValue(value.GetValueOrDefault().Ticks / TicksPerMicroseconds);
        }
    }
}
