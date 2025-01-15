using Meziantou.Framework.HumanReadable.Utils;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class EnumerableConverterFactory : HumanReadableConverterFactory
{
    public override bool CanConvert(Type type) => GetValueType(type) != null;

    private static Type? GetValueType(Type type)
    {
        foreach (var iface in type.GetAllInterfaces())
        {
            if (!iface.IsGenericType)
                continue;

            if (iface.GetGenericTypeDefinition() != typeof(IEnumerable<>))
                continue;

            return iface.GenericTypeArguments[0];
        }

        return null;
    }

    public override HumanReadableConverter? CreateConverter(Type typeToConvert, HumanReadableSerializerOptions options)
    {
        return (HumanReadableConverter)Activator.CreateInstance(typeof(EnumerableConverter<>).MakeGenericType(GetValueType(typeToConvert)!))!;
    }
}
