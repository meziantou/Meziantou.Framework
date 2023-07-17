using System.Diagnostics;
using Meziantou.Framework.HumanReadable.Utils;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class EnumerableKeyValuePairConverterFactory : HumanReadableConverterFactory
{
    public override bool CanConvert(Type type) => GetValueType(type) != null;

    public override HumanReadableConverter? CreateConverter(Type typeToConvert, HumanReadableSerializerOptions options)
    {
        var type = GetValueType(typeToConvert)!;
        return (HumanReadableConverter?)Activator.CreateInstance(typeof(EnumerableKeyValuePairConverter<>).MakeGenericType(type));
    }

    private static Type? GetValueType(Type type)
    {
        // IEnumerable<KeyValuePair<string, T>>
        foreach (var iface in type.GetAllInterfaces())
        {
            if (!iface.IsGenericType)
                continue;

            if (iface.GetGenericTypeDefinition() != typeof(IEnumerable<>))
                continue;

            var enumerableType = iface.GenericTypeArguments[0];
            if (!enumerableType.IsGenericType)
                continue;

            if (enumerableType.GetGenericTypeDefinition() != typeof(KeyValuePair<,>))
                continue;

            var keyType = enumerableType.GenericTypeArguments[0];
            if (keyType == typeof(string))
                return enumerableType.GenericTypeArguments[1];
        }

        return null;
    }

    [SuppressMessage("Performance", "CA1812", Justification = "The class is instantiated using Activator.CreateInstance")]
    private sealed class EnumerableKeyValuePairConverter<T> : HumanReadableConverter<IEnumerable<KeyValuePair<string, T>>>
    {
        protected override void WriteValue(HumanReadableTextWriter writer, IEnumerable<KeyValuePair<string, T>>? value, HumanReadableSerializerOptions options)
        {
            Debug.Assert(value != null);
            if (options.DictionaryKeyOrder != null)
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
}
