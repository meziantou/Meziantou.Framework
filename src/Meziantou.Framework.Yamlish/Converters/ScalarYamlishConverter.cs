using System.ComponentModel;

namespace Meziantou.Framework.Yamlish;

internal sealed class ScalarYamlishConverter<T>(Func<string, T>? parser = null, Func<T, string>? formatter = null) : YamlishConverter<T>
{
    private static readonly TypeConverter TypeConverter = TypeDescriptor.GetConverter(typeof(T));

    public override T Read(YamlishNode node, YamlishSerializerOptions options)
    {
        var value = ConverterUtilities.GetScalarValue(node, typeof(T));
        try
        {
            if (parser is not null)
                return parser(value);

            return (T)(TypeConverter.ConvertFromInvariantString(value) ?? throw CannotConvert(value));
        }
        catch (Exception exception) when (exception is not FormatException)
        {
            throw CannotConvert(value, exception);
        }
    }

    public override YamlishNode Write(T value, YamlishSerializerOptions options)
    {
        if (formatter is not null)
            return new YamlishScalar(formatter(value));

        string result;
        if (value is IFormattable formattable)
        {
            result = formattable.ToString(format: null, CultureInfo.InvariantCulture);
        }
        else
        {
            result = TypeConverter.ConvertToInvariantString(value) ?? string.Empty;
        }

        return new YamlishScalar(result);
    }

    private static FormatException CannotConvert(string value, Exception? innerException = null)
        => new($"Cannot convert '{value}' to '{typeof(T)}'.", innerException);
}
