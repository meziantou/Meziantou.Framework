using System.Collections;
using System.Collections.Specialized;
using System.Diagnostics;

namespace Meziantou.Framework.HumanReadable.Converters;

// Non-generic dictionaries. The generic ones are handled by the enumerable converters, which are registered before this one.
internal sealed class DictionaryConverter : HumanReadableConverter<IDictionary>
{
    protected override void WriteValue(HumanReadableTextWriter writer, IDictionary? value, HumanReadableSerializerOptions options)
    {
        Debug.Assert(value is not null);

        IEnumerable<DictionaryEntry> entries = [.. value.Cast<DictionaryEntry>()];

        // The enumeration order of hash-based collections depends on the hash codes of the keys, which are randomized for strings
        var keyOrder = options.DictionaryKeyOrder ?? (value is Hashtable or HybridDictionary ? StringComparer.Ordinal : null);
        if (keyOrder is not null)
        {
            entries = entries.OrderBy(entry => GetKeyText(entry.Key), keyOrder);
        }

        var hasItem = false;
        foreach (var entry in entries)
        {
            if (!hasItem)
            {
                writer.StartArray();
                hasItem = true;
            }

            writer.StartArrayItem();
            HumanReadableSerializer.Serialize(writer, entry, typeof(DictionaryEntry), options);
            writer.EndArrayItem();
        }

        if (hasItem)
        {
            writer.EndArray();
        }
        else
        {
            writer.WriteEmptyArray();
        }
    }

    private static string GetKeyText(object key)
    {
        return key switch
        {
            string str => str,
            IFormattable formattable => formattable.ToString(format: null, CultureInfo.InvariantCulture),
            _ => key.ToString() ?? "",
        };
    }
}
