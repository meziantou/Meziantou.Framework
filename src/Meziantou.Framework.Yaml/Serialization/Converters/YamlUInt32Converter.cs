namespace Meziantou.Framework.Yaml.Serialization.Converters;

internal sealed class YamlUInt32Converter : YamlConverter<uint>
{
    public static YamlUInt32Converter Instance { get; } = new();

    public override uint Read(YamlReader reader)
    {
        if (reader.TokenType != YamlTokenType.Scalar)
        {
            throw YamlThrowHelper.ThrowExpectedScalar(reader);
        }

        if (!YamlScalar.TryParseUInt32(reader, out var parsed))
        {
            throw YamlThrowHelper.ThrowInvalidUInt32Scalar(reader);
        }

        reader.Read();
        return parsed;
    }

    public override void Write(YamlWriter writer, uint value)
        => writer.WriteScalar(value.ToString(CultureInfo.InvariantCulture));
}
