namespace Meziantou.Framework.Yaml.Serialization.Converters;

internal sealed class YamlUInt16Converter : YamlConverter<ushort>
{
    public static YamlUInt16Converter Instance { get; } = new();

    public override ushort Read(YamlReader reader)
    {
        if (reader.TokenType != YamlTokenType.Scalar)
        {
            throw YamlThrowHelper.ThrowExpectedScalar(reader);
        }

        if (!YamlScalar.TryParseUInt64(reader, out var parsed) || parsed > ushort.MaxValue)
        {
            throw YamlThrowHelper.ThrowInvalidUInt16Scalar(reader);
        }

        reader.Read();
        return (ushort)parsed;
    }

    public override void Write(YamlWriter writer, ushort value)
        => writer.WriteScalar(value.ToString(CultureInfo.InvariantCulture));
}
