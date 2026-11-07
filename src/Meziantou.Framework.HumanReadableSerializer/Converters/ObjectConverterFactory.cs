using System.ComponentModel;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class ObjectConverterFactory : HumanReadableConverterFactory
{
    public override bool CanConvert(Type type) => true;

    public override HumanReadableConverter? CreateConverter(Type typeToConvert, HumanReadableSerializerOptions options)
    {
        return (HumanReadableConverter?)Activator.CreateInstance(typeof(ObjectConverter<>).MakeGenericType(typeToConvert));
    }

    [SuppressMessage("Performance", "CA1812", Justification = "The class is instantiated using Activator.CreateInstance")]
    private sealed class ObjectConverter<T> : HumanReadableConverter<T>
    {
        private static readonly TypeConverter? TypeConverter = GetTypeConverter();

        public override bool HandleNull => true;

        private static TypeConverter? GetTypeConverter()
        {
            var typeConverter = TypeDescriptor.GetConverter(typeof(T));

            // Some converters cannot produce a meaningful string, so the members are serialized instead:
            // - ReferenceConverter (used for interfaces) and ComponentConverter (used for IComponent) return an empty string without a site
            // - ExpandableObjectConverter and CollectionConverter return the name of the type
            if (typeConverter is null or ReferenceConverter or ExpandableObjectConverter or CollectionConverter || typeConverter.GetType() == typeof(TypeConverter))
                return null;

            if (typeConverter.CanConvertTo(typeof(string)))
                return typeConverter;

            return null;
        }

        protected override void WriteValue(HumanReadableTextWriter writer, T? value, HumanReadableSerializerOptions options)
        {
            if (value is null)
            {
                writer.WriteNullValue();
                return;
            }

            if (TypeConverter is not null)
            {
                var stringValue = TypeConverter.ConvertToInvariantString(context: null, value);
                writer.WriteValue(stringValue ?? "");
            }
            else if (value is IConvertible convertible)
            {
                var stringValue = convertible.ToString(CultureInfo.InvariantCulture);
                writer.WriteValue(stringValue);
            }
            else
            {
                var type = typeof(T);
                var hasMember = false;

                var members = options.GetMembers(type);
                foreach (var member in members)
                {
                    if (!member.TryGetValue(value, out var memberValue))
                        continue;

                    if (!hasMember)
                    {
                        writer.StartObject();
                        hasMember = true;
                    }

                    writer.WritePropertyName(member.Name);
                    member.WriteValue(writer, memberValue, options);
                }

                if (hasMember)
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
}
