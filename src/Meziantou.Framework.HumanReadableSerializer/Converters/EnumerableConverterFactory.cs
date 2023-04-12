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
        return (HumanReadableConverter)Activator.CreateInstance(typeof(EnumerableConverter<>).MakeGenericType(GetValueType(typeToConvert)));
    }

    [SuppressMessage("Performance", "CA1812", Justification = "The class is instantiated using Activator.CreateInstance")]
    private sealed class EnumerableConverter<T> : HumanReadableConverter<IEnumerable<T>>
    {
        protected override void WriteValue(HumanReadableTextWriter writer, IEnumerable<T> value, HumanReadableSerializerOptions options)
        {
            var hasItem = false;

            foreach (var item in value)
            {
                if (!hasItem)
                {
                    writer.StartArray();
                    hasItem = true;
                }

                writer.StartArrayItem();
                HumanReadableSerializer.Serialize(writer, item, item?.GetType() ?? typeof(T), options);
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
    }
}
