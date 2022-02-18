using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meziantou.Framework.ChromiumTracing
{
    [SuppressMessage("Usage", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated dynamically")]
    internal sealed class DateTimeOffsetToTimestampJsonConverter : JsonConverter<DateTimeOffset>
    {
        internal const long TicksPerMicroseconds = 10;

        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var value = reader.GetInt64();
            return new DateTimeOffset(value * TicksPerMicroseconds, TimeSpan.Zero);
        }

        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
        {
            writer.WriteNumberValue(value.Ticks / TicksPerMicroseconds);
        }
    }
}
