using System.Collections.Specialized;
using System.Diagnostics;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class NameValueCollectionConverter : HumanReadableConverter<NameValueCollection>
{
    protected override void WriteValue(HumanReadableTextWriter writer, NameValueCollection? value, HumanReadableSerializerOptions options)
    {
        Debug.Assert(value is not null);

        if (options.DictionaryKeyOrder is not null)
        {
            var dict = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string item in value.Keys)
            {
                dict.Add(item, value[item]);
            }

            HumanReadableSerializer.Serialize(writer, dict, options);
        }
        else
        {
            var hasItem = false;
            for (var i = 0; i < value.Count; i++)
            {
                if (!hasItem)
                {
                    writer.StartObject();
                    hasItem = true;
                }

                writer.WritePropertyName(value.GetKey(i));
                var values = value.GetValues(i);
                if (values.Length == 1)
                {
                    HumanReadableSerializer.Serialize(writer, values[0], options);
                }
                else
                {
                    HumanReadableSerializer.Serialize(writer, values, options);
                }
            }

            if (hasItem)
            {
                writer.EndObject();
            }
            else
            {
                writer.WriteEmptyObject();
            }
        }
    }
}
