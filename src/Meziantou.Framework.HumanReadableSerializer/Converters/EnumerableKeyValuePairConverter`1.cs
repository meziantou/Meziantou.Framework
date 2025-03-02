using System.Diagnostics;

namespace Meziantou.Framework.HumanReadable.Converters;

[SuppressMessage("Performance", "CA1812", Justification = "The class is instantiated using Activator.CreateInstance")]
internal sealed class EnumerableKeyValuePairConverter<T> : HumanReadableConverter<IEnumerable<KeyValuePair<string, T>>>
{
    protected override void WriteValue(HumanReadableTextWriter writer, IEnumerable<KeyValuePair<string, T>>? value, HumanReadableSerializerOptions options)
    {
        WriteValueCore(writer, value, options);
    }

    internal static void WriteValueCore(HumanReadableTextWriter writer, IEnumerable<KeyValuePair<string, T>>? value, HumanReadableSerializerOptions options)
    {
        Debug.Assert(value is not null);
        if (options.DictionaryKeyOrder is not null)
        {
            value = value.OrderBy(value => value.Key, options.DictionaryKeyOrder);
        }

        var hasItem = false;
        foreach (var prop in value)
        {
            if (!hasItem)
            {
                writer.StartObject();
                hasItem = true;
            }

            writer.WritePropertyName(prop.Key);
            HumanReadableSerializer.Serialize(writer, prop.Value, options);
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
