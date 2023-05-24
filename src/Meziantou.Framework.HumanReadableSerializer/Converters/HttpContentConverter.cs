using System.Diagnostics;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class HttpContentConverter : HumanReadableConverter<HttpContent>
{
    protected override void WriteValue(HumanReadableTextWriter writer, HttpContent? value, HumanReadableSerializerOptions options)
    {
        Debug.Assert(value != null);

        writer.StartObject();

        var hasHeaders = value.Headers.Any();
        var hasMultipleContent = value is IEnumerable<HttpContent>;
        if (hasHeaders)
        {
            writer.WritePropertyName("Headers");
            HumanReadableSerializer.Serialize(writer, value.Headers, options);
        }

        if (hasHeaders || hasMultipleContent)
        {
            writer.WritePropertyName("Content");
        }
        if (value is IEnumerable<HttpContent> collection)
        {
            options.GetConverter(typeof(IEnumerable<HttpContent>)).WriteValue(writer, collection, options);
        }
        else
        {
            var charSet = value.Headers.ContentType?.CharSet;
            if (!string.IsNullOrEmpty(charSet) || value is StringContent or FormUrlEncodedContent)
            {
                var str = value.ReadAsStringAsync().Result;
                writer.WriteValue(str);
            }
            else
            {
                var bytes = value.ReadAsByteArrayAsync().Result;
                options.GetConverter(typeof(byte[])).WriteValue(writer, bytes, options);
            }
        }
        writer.EndObject();
    }
}
