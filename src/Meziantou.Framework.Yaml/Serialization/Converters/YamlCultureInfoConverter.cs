namespace Meziantou.Framework.Yaml.Serialization.Converters;

internal sealed class YamlCultureInfoConverter : YamlConverter<CultureInfo?>
{
    public static YamlCultureInfoConverter Instance { get; } = new();

    public override CultureInfo? Read(YamlReader reader)
    {
        if (reader.TokenType != YamlTokenType.Scalar)
        {
            throw YamlThrowHelper.ThrowExpectedScalar(reader);
        }

        if (YamlScalar.IsNull(reader))
        {
            reader.Read();
            return null;
        }

        var text = reader.ScalarValue;
        if (!YamlScalar.TryParseCultureInfo(text, out var result))
        {
            throw YamlThrowHelper.ThrowInvalidCultureInfoScalar(reader);
        }

        reader.Read();
        return result;
    }

    public override void Write(YamlWriter writer, CultureInfo? value)
    {
        writer.WriteScalar(value?.Name);
    }
}
