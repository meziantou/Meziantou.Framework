namespace Meziantou.Framework.Yaml.Serialization.Converters;

internal sealed class YamlByteConverter : YamlConverter<byte>
{
    public static YamlByteConverter Instance { get; } = new();

    public override byte Read(YamlReader reader)
    {
        if (reader.TokenType != YamlTokenType.Scalar)
        {
            throw YamlThrowHelper.ThrowExpectedScalar(reader);
        }

        if (!YamlScalar.TryParseUInt64(reader, out var parsed) || parsed > byte.MaxValue)
        {
            throw YamlThrowHelper.ThrowInvalidByteScalar(reader);
        }

        reader.Read();
        return (byte)parsed;
    }

    public override void Write(YamlWriter writer, byte value)
        => writer.WriteScalar(value.ToString(CultureInfo.InvariantCulture));
}
