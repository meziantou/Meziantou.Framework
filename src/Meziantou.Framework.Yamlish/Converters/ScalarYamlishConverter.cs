namespace Meziantou.Framework.Yamlish;

internal abstract class ScalarYamlishConverter<T> : YamlishConverter<T>
{
    public sealed override T Read(YamlishNode node, YamlishSerializerOptions options)
    {
        var value = ConverterUtilities.GetScalarValue(node, typeof(T));
        try
        {
            return Parse(value);
        }
        catch (Exception exception) when (exception is not FormatException)
        {
            throw new FormatException($"Cannot convert '{value}' to '{typeof(T)}'.", exception);
        }
    }

    public sealed override YamlishNode Write(T value, YamlishSerializerOptions options)
    {
        return new YamlishScalar(Format(value));
    }

    protected abstract T Parse(string value);

    protected abstract string Format(T value);
}
