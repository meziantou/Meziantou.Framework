namespace Meziantou.Framework.Yaml.Serialization.Converters;

internal sealed class YamlInt64Converter : YamlConverter<long>
{
    public static YamlInt64Converter Instance { get; } = new();

    public override long Read(YamlReader reader)
    {
        if (reader.TokenType != YamlTokenType.Scalar)
        {
            throw YamlThrowHelper.ThrowExpectedScalar(reader);
        }

        if (!YamlScalar.TryParseInt64(reader, out var result))
        {
            throw YamlThrowHelper.ThrowInvalidIntegerScalar(reader);
        }

        reader.Read();
        return result;
    }

    public override void Write(YamlWriter writer, long value)
    {
        writer.WriteScalar(value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}
