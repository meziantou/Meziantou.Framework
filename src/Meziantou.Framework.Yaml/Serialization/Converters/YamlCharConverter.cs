namespace Meziantou.Framework.Yaml.Serialization.Converters;

internal sealed class YamlCharConverter : YamlConverter<char>
{
    public static YamlCharConverter Instance { get; } = new();

    public override char Read(YamlReader reader)
    {
        if (reader.TokenType != YamlTokenType.Scalar)
        {
            throw YamlThrowHelper.ThrowExpectedScalar(reader);
        }

        var text = reader.ScalarValue ?? string.Empty;
        if (text.Length != 1)
        {
            throw YamlThrowHelper.ThrowInvalidCharScalar(reader, text);
        }

        reader.Read();
        return text[0];
    }

    public override void Write(YamlWriter writer, char value)
        => writer.WriteScalar(value.ToString());
}
