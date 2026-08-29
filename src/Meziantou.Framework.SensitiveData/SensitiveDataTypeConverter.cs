using System.ComponentModel;

namespace Meziantou.Framework;

[SuppressMessage("Usage", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated dynamically")]
internal sealed class SensitiveDataTypeConverter : TypeConverter
{
    private readonly Type _type;

    public SensitiveDataTypeConverter()
        : this(typeof(SensitiveData<char>))
    {
    }

    // TypeDescriptor uses this constructor when a converter declared by TypeConverterAttribute
    // exposes it, and passes the type being described. SensitiveDataTypeConverter is declared on
    // the open generic SensitiveData<T>, so without it the converter cannot tell SensitiveData<char>
    // apart from any other construction of SensitiveData<T>.
    public SensitiveDataTypeConverter(Type type)
    {
        _type = type;
    }

    private bool IsSupportedType => _type == typeof(SensitiveData<char>);

    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
    {
        return IsSupportedType && sourceType == typeof(string);
    }

    public override bool CanConvertTo(ITypeDescriptorContext? context, [NotNullWhen(true)] Type? destinationType)
    {
        return false;
    }

    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
    {
        // Converting away from SensitiveData is never supported: the whole point of the type is that
        // its contents do not leak into a string. NotSupportedException is what TypeConverter
        // documents for a conversion it cannot perform, and what callers that probe a converter
        // defensively catch.
        throw new NotSupportedException($"Cannot convert '{typeof(SensitiveData<char>)}' to '{destinationType}'. Revealing the contents has to be explicit.");
    }

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is string str)
        {
            if (!IsSupportedType)
                throw new NotSupportedException($"Cannot convert a string to '{_type}'. Only '{typeof(SensitiveData<char>)}' can be created from a string.");

            return SensitiveData.Create(str);
        }

        return base.ConvertFrom(context, culture, value);
    }
}
