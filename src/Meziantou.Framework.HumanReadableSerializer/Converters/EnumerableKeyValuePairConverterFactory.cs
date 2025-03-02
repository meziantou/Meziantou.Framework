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
}
