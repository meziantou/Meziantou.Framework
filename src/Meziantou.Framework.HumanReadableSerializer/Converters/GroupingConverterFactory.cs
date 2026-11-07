using System.Diagnostics;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class GroupingConverterFactory : HumanReadableConverterFactory
{
    public override bool CanConvert(Type type) => GetGroupingType(type) is not null;

    public override HumanReadableConverter? CreateConverter(Type typeToConvert, HumanReadableSerializerOptions options)
    {
        var groupingType = GetGroupingType(typeToConvert)!;
        return (HumanReadableConverter?)Activator.CreateInstance(typeof(GroupingConverter<,>).MakeGenericType(groupingType.GenericTypeArguments));
    }

    private static Type? GetGroupingType(Type type)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IGrouping<,>))
            return type;

        foreach (var iface in type.GetInterfaces())
        {
            if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IGrouping<,>))
                return iface;
        }

        return null;
    }

    [SuppressMessage("Performance", "CA1812", Justification = "The class is instantiated using Activator.CreateInstance")]
    private sealed class GroupingConverter<TKey, TElement> : HumanReadableConverter<IGrouping<TKey, TElement>>
    {
        protected override void WriteValue(HumanReadableTextWriter writer, IGrouping<TKey, TElement>? value, HumanReadableSerializerOptions options)
        {
            Debug.Assert(value is not null);

            writer.StartObject();
            writer.WritePropertyName("Key");
            HumanReadableSerializer.Serialize(writer, value.Key, value.Key?.GetType() ?? typeof(TKey), options);
            writer.WritePropertyName("Values");
            EnumerableConverter<TElement>.WriteValueCore(writer, value, options);
            writer.EndObject();
        }
    }
}
