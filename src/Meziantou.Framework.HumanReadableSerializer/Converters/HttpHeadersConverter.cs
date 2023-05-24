using System.Diagnostics;
using System.Net.Http.Headers;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class HttpHeadersConverter : HumanReadableConverter<HttpHeaders>
{
    protected override void WriteValue(HumanReadableTextWriter writer, HttpHeaders? value, HumanReadableSerializerOptions options)
    {
        Debug.Assert(value != null);

        var hasValue = false;
        foreach (var header in value)
        {
            if (!hasValue)
            {
                writer.StartObject();
                hasValue = true;
            }

            writer.WritePropertyName(header.Key);

            var valueCount = header.Value.Count();
            if (valueCount == 1)
            {
                writer.WriteValue(header.Value.First());
            }
            else
            {
                HumanReadableSerializer.Serialize(writer, header.Value, options);
            }
        }

        if (hasValue)
        {
            writer.EndObject();
        }
        else
        {
            writer.WriteEmptyObject();
        }
    }
}
