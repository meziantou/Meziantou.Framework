using System.Net.Http.Headers;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class HttpHeadersConverter : HumanReadableConverter<HttpHeaders>
{
    protected override void WriteValue(HumanReadableTextWriter writer, HttpHeaders value, HumanReadableSerializerOptions options)
    {
        writer.StartObject();
        foreach (var header in value)
        {
            writer.WritePropertyName(header.Key);

            var valueCount = header.Value.Count();
            if (valueCount < 2)
            {
                writer.WriteValue(header.Value.FirstOrDefault());
            }
            else
            {
                HumanReadableSerializer.Serialize(writer, header.Value, options);
            }
        }

        writer.EndObject();
    }
}
