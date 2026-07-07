namespace Meziantou.Framework.Yaml.Serialization.Converters;

internal sealed class YamlBooleanConverter : YamlConverter<bool>
{
    public static YamlBooleanConverter Instance { get; } = new();

    public override bool Read(YamlReader reader)
    {
        if (reader.TokenType != YamlTokenType.Scalar)
        {
            throw YamlThrowHelper.ThrowExpectedScalar(reader);
        }

        if (!YamlScalar.TryParseBool(reader, out var result))
        {
            throw YamlThrowHelper.ThrowInvalidBooleanScalar(reader);
        }

        reader.Read();
        return result;
    }

    public override void Write(YamlWriter writer, bool value)
    {
        writer.WriteScalar(value);
    }
}
