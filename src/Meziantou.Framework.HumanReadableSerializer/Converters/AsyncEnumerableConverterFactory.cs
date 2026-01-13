using System.Diagnostics;
using Meziantou.Framework.HumanReadable.Utils;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class AsyncEnumerableConverterFactory : HumanReadableConverterFactory
{
    public override bool CanConvert(Type type) => GetValueType(type) != null;

    private static Type? GetValueType(Type type)
    {
        foreach (var iface in type.GetAllInterfaces())
        {
            if (!iface.IsGenericType)
                continue;

            if (iface.GetGenericTypeDefinition() != typeof(IAsyncEnumerable<>))
                continue;

            return iface.GenericTypeArguments[0];
        }

        return null;
    }

    public override HumanReadableConverter? CreateConverter(Type typeToConvert, HumanReadableSerializerOptions options)
    {
        return (HumanReadableConverter)Activator.CreateInstance(typeof(AsyncEnumerableConverter<>).MakeGenericType(GetValueType(typeToConvert)!))!;
    }

    [SuppressMessage("Performance", "CA1812", Justification = "The class is instantiated using Activator.CreateInstance")]
    private sealed class AsyncEnumerableConverter<T> : HumanReadableConverter<IAsyncEnumerable<T>>
    {
        protected override void WriteValue(HumanReadableTextWriter writer, IAsyncEnumerable<T>? value, HumanReadableSerializerOptions options)
        {
            Debug.Assert(value is not null);
            EnumerableConverter<T>.WriteValueCore(writer, value.ToBlockingEnumerable(), options);
        }
    }
}
