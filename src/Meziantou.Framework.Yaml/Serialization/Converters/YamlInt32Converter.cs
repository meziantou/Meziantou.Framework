namespace Meziantou.Framework.Yaml.Serialization.Converters;

internal sealed class YamlInt32Converter : YamlConverter<int>
{
    public static YamlInt32Converter Instance { get; } = new();

    public override int Read(YamlReader reader)
    {
        if (reader.TokenType != YamlTokenType.Scalar)
        {
            throw YamlThrowHelper.ThrowExpectedScalar(reader);
        }

        if (!YamlScalar.TryParseInt32(reader, out var result))
        {
            throw YamlThrowHelper.ThrowInvalidIntegerScalar(reader);
        }

        reader.Read();
        return result;
    }

    public override void Write(YamlWriter writer, int value)
    {
        writer.WriteScalar(value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}
