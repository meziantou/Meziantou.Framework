namespace Meziantou.Framework.Yaml.Serialization.Converters;

internal sealed class YamlDecimalConverter : YamlConverter<decimal>
{
    public static YamlDecimalConverter Instance { get; } = new();

    public override decimal Read(YamlReader reader)
    {
        if (reader.TokenType != YamlTokenType.Scalar)
        {
            throw YamlThrowHelper.ThrowExpectedScalar(reader);
        }

        if (!YamlScalar.TryParseDecimal(reader, out var parsed))
        {
            throw YamlThrowHelper.ThrowInvalidDecimalScalar(reader);
        }

        reader.Read();
        return parsed;
    }

    public override void Write(YamlWriter writer, decimal value)
        => writer.WriteScalar(value.ToString(CultureInfo.InvariantCulture));
}
