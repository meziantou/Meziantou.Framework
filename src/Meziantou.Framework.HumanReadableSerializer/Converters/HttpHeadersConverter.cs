using System.Diagnostics;
using System.Net.Http.Headers;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class HttpHeadersConverter : HumanReadableConverter<HttpHeaders>
{
    protected override void WriteValue(HumanReadableTextWriter writer, HttpHeaders? value, HumanReadableSerializerOptions options)
    {
        Debug.Assert(value != null);
        var hasValue = false;

#if NETSTANDARD2_0 || NETFRAMEWORK
        IEnumerable<KeyValuePair<string, IEnumerable<string>>> values = value;
#else
        IEnumerable<KeyValuePair<string, HeaderStringValues>> values = value.NonValidated;
#endif

        if (options.PropertyOrder != null)
        {
            values = values.OrderBy(o => o.Key, options.PropertyOrder);
        }

        foreach (var header in values)
        {
            if (!hasValue)
            {
                writer.StartObject();
                hasValue = true;
            }

            writer.WritePropertyName(header.Key);

#if NETSTANDARD2_0 || NETFRAMEWORK
            var valueCount = header.Value.Count();
#else
            var valueCount = header.Value.Count;
#endif
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
