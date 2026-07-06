namespace Meziantou.Framework.Yaml.Serialization.Converters;

internal sealed class YamlUInt64Converter : YamlConverter<ulong>
{
    public static YamlUInt64Converter Instance { get; } = new();

    public override ulong Read(YamlReader reader)
    {
        if (reader.TokenType != YamlTokenType.Scalar)
        {
            throw YamlThrowHelper.ThrowExpectedScalar(reader);
        }

        if (!YamlScalar.TryParseUInt64(reader, out var parsed))
        {
            throw YamlThrowHelper.ThrowInvalidUInt64Scalar(reader);
        }

        reader.Read();
        return parsed;
    }

    public override void Write(YamlWriter writer, ulong value)
        => writer.WriteScalar(value.ToString(CultureInfo.InvariantCulture));
}
