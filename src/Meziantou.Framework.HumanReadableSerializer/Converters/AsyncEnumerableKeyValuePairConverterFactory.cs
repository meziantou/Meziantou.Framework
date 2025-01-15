using Meziantou.Framework.HumanReadable.Utils;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class AsyncEnumerableKeyValuePairConverterFactory : HumanReadableConverterFactory
{
    public override bool CanConvert(Type type) => GetValueType(type) != null;

    public override HumanReadableConverter? CreateConverter(Type typeToConvert, HumanReadableSerializerOptions options)
    {
        var type = GetValueType(typeToConvert)!;
        return (HumanReadableConverter?)Activator.CreateInstance(typeof(AsyncEnumerableKeyValuePairConverter<>).MakeGenericType(type));
    }

    private static Type? GetValueType(Type type)
    {
        // IAsyncEnumerable<KeyValuePair<string, T>>
        foreach (var iface in type.GetAllInterfaces())
        {
            if (!iface.IsGenericType)
                continue;

            if (iface.GetGenericTypeDefinition() != typeof(IAsyncEnumerable<>))
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
    private sealed class AsyncEnumerableKeyValuePairConverter<T> : HumanReadableConverter<IAsyncEnumerable<KeyValuePair<string, T>>>
    {
        protected override void WriteValue(HumanReadableTextWriter writer, IAsyncEnumerable<KeyValuePair<string, T>>? value, HumanReadableSerializerOptions options)
        {
            EnumerableKeyValuePairConverter<T>.WriteValueCore(writer, value.ToBlockingEnumerable(), options);
        }
    }
}
