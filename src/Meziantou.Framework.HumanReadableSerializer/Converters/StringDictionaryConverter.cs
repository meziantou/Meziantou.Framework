using System.Collections.Specialized;
using System.Diagnostics;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class StringDictionaryConverter : HumanReadableConverter<StringDictionary>
{
    protected override void WriteValue(HumanReadableTextWriter writer, StringDictionary? value, HumanReadableSerializerOptions options)
    {
        Debug.Assert(value is not null);

        // StringDictionary is backed by a Hashtable, so its enumeration order depends on the randomized string hash codes
        var keys = value.Keys.Cast<string>().Order(options.DictionaryKeyOrder ?? StringComparer.Ordinal).ToArray();
        if (keys.Length is 0)
        {
            writer.WriteEmptyObject();
            return;
        }

        writer.StartObject();
        foreach (var key in keys)
        {
            writer.WritePropertyName(key);
            HumanReadableSerializer.Serialize(writer, value[key], typeof(string), options);
        }

        writer.EndObject();
    }
}
