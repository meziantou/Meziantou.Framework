using System.ComponentModel;
using System.Globalization;
using System.Reflection;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class ObjectConverter : HumanReadableConverter
{
    public override bool HandleNull => true;

    public override bool CanConvert(Type type) => true;

    public override void WriteValue(HumanReadableTextWriter writer, object? value, HumanReadableSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        // Use custom TypeConverter if any
        var typeConverter = TypeDescriptor.GetConverter(value.GetType());
        if (typeConverter != null && typeConverter.GetType() != typeof(TypeConverter) && typeConverter.CanConvertTo(typeof(string)))
        {
            var stringValue = typeConverter.ConvertToInvariantString(context: null, value);
            writer.WriteValue(stringValue);
        }
        else if (value is IConvertible convertible)
        {
            var stringValue = convertible.ToString(CultureInfo.InvariantCulture);
            writer.WriteValue(stringValue);
        }
        else
        {
            var type = value.GetType();
            var hasProp = false;
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanRead)
                    continue;

                // Exclude indexers
                if (prop.GetIndexParameters().Length > 0)
                    continue;

                if (prop.GetCustomAttribute<HumanReadableIgnoreAttribute>() != null)
                    continue;

                if (!hasProp)
                {
                    writer.StartObject();
                    hasProp = true;
                }

                var propertyValue = prop.GetValue(value);
                var propertyType = propertyValue?.GetType() ?? prop.PropertyType;

                var propertyName = prop.GetCustomAttribute<HumanReadablePropertyNameAttribute>()?.Name ?? prop.Name;
                writer.WritePropertyName(propertyName);

                var converterAttribute = prop.GetCustomAttribute<HumanReadableConverterAttribute>();
                if (converterAttribute != null)
                {
                    if (!typeof(HumanReadableConverter).IsAssignableFrom(converterAttribute.ConverterType))
                        throw new HumanReadableSerializerException($"The converter '{converterAttribute.ConverterType}' must inherit from '{typeof(HumanReadableConverter).AssemblyQualifiedName}'");

                    var instance = (HumanReadableConverter)Activator.CreateInstance(converterAttribute.ConverterType);
                    if (!instance.HandleNull)
                    {
                        instance = new NullConverterWrapper(instance);
                    }

                    if (!instance.CanConvert(propertyType))
                        throw new HumanReadableSerializerException($"The converter '{converterAttribute.ConverterType}' is not compatible with '{propertyType.FullName}'");

                    instance.WriteValue(writer, propertyValue, options);
                }
                else
                {
                    if (propertyType == typeof(object))
                    {
                        writer.WriteEmptyObject();
                    }
                    else
                    {
                        var converter = options.GetConverter(propertyType);
                        converter.WriteValue(writer, propertyValue, options);
                    }
                }
            }

            if (hasProp)
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
