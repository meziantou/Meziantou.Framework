using System.ComponentModel;
using System.Globalization;

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
            if (typeConverter != null && typeConverter.GetType() != typeof(TypeConverter) && typeConverter.CanConvertTo(typeof(string)))
                return typeConverter;

            return null;
        }

        protected override void WriteValue(HumanReadableTextWriter writer, T? value, HumanReadableSerializerOptions options)
        {
            if (value == null)
            {
                writer.WriteNullValue();
                return;
            }

            if (TypeConverter != null)
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
                    var memberValue = member.GetValue(value);
                    if (member.MustIgnore(memberValue))
                        continue;

                    if (!hasMember)
                    {
                        writer.StartObject();
                        hasMember = true;
                    }

                    writer.WritePropertyName(member.Name);

                    if (member.Converter != null)
                    {
                        member.Converter.WriteValue(writer, memberValue, options);
                    }
                    else
                    {
                        var actualType = memberValue?.GetType() ?? member.MemberType;
                        if (actualType == typeof(object))
                        {
                            if (memberValue == null)
                            {
                                writer.WriteNullValue();
                            }
                            else
                            {
                                writer.WriteEmptyObject();
                            }
                        }
                        else
                        {
                            HumanReadableSerializer.Serialize(writer, memberValue, actualType, options);
                        }
                    }
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
