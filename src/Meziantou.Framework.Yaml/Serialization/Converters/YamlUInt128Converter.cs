namespace Meziantou.Framework.Yaml.Serialization.Converters;

internal sealed class YamlUInt128Converter : YamlConverter<UInt128>
{
    public static YamlUInt128Converter Instance { get; } = new();

    public override UInt128 Read(YamlReader reader)
    {
        if (reader.TokenType != YamlTokenType.Scalar)
        {
            throw YamlThrowHelper.ThrowExpectedScalar(reader);
        }

        var text = reader.ScalarValue;
        if (!UInt128.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
        {
            throw YamlThrowHelper.ThrowInvalidUInt128Scalar(reader);
        }

        reader.Read();
        return result;
    }

    public override void Write(YamlWriter writer, UInt128 value)
    {
        writer.WriteScalar(value.ToString(CultureInfo.InvariantCulture));
    }
}
