using System.Collections;
using System.Collections.Specialized;
using System.Diagnostics;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class StringDictionaryConverter : HumanReadableConverter<StringDictionary>
{
    protected override void WriteValue(HumanReadableTextWriter writer, StringDictionary? value, HumanReadableSerializerOptions options)
    {
        Debug.Assert(value is not null);

        if (options.DictionaryKeyOrder is not null)
        {
            var dict = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (string item in value.Keys)
            {
                dict.Add(item, value[item]);
            }

            HumanReadableSerializer.Serialize(writer, dict, options);
        }
        else
        {
            var hasItem = false;
            var enumerator = (IDictionaryEnumerator)value.GetEnumerator();
            while (enumerator.MoveNext())
            {
                if (!hasItem)
                {
                    writer.StartObject();
                    hasItem = true;
                }

                writer.WritePropertyName((string)enumerator.Key);
                HumanReadableSerializer.Serialize(writer, enumerator.Value, options);
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
