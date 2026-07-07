namespace Meziantou.Framework.Yaml.Serialization.Converters;

internal sealed class YamlInt128Converter : YamlConverter<Int128>
{
    public static YamlInt128Converter Instance { get; } = new();

    public override Int128 Read(YamlReader reader)
    {
        if (reader.TokenType != YamlTokenType.Scalar)
        {
            throw YamlThrowHelper.ThrowExpectedScalar(reader);
        }

        var text = reader.ScalarValue;
        if (!Int128.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
        {
            throw YamlThrowHelper.ThrowInvalidInt128Scalar(reader);
        }

        reader.Read();
        return result;
    }

    public override void Write(YamlWriter writer, Int128 value)
    {
        writer.WriteScalar(value);
    }
}
