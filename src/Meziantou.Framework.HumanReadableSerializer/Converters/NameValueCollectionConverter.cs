using System.Collections.Specialized;
using System.Diagnostics;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class NameValueCollectionConverter : HumanReadableConverter<NameValueCollection>
{
    protected override void WriteValue(HumanReadableTextWriter writer, NameValueCollection? value, HumanReadableSerializerOptions options)
    {
        Debug.Assert(value is not null);

        if (value.Count is 0)
        {
            writer.WriteEmptyObject();
            return;
        }

        // Sort the indices rather than copying the entries: the collection allows a null key and several values per key
        IEnumerable<int> indices = Enumerable.Range(0, value.Count);
        if (options.DictionaryKeyOrder is not null)
        {
            indices = indices.OrderBy(index => value.GetKey(index) ?? "", options.DictionaryKeyOrder);
        }

        writer.StartObject();
        foreach (var index in indices)
        {
            writer.WritePropertyName(value.GetKey(index) ?? "");
            var values = value.GetValues(index);
            if (values is null)
            {
                writer.WriteNullValue();
            }
            else if (values.Length is 1)
            {
                HumanReadableSerializer.Serialize(writer, values[0], options);
            }
            else
            {
                HumanReadableSerializer.Serialize(writer, values, options);
            }
        }

        writer.EndObject();
    }
}
