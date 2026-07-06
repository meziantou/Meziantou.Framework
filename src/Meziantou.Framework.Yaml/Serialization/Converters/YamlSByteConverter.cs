namespace Meziantou.Framework.Yaml.Serialization.Converters;

internal sealed class YamlSByteConverter : YamlConverter<sbyte>
{
    public static YamlSByteConverter Instance { get; } = new();

    public override sbyte Read(YamlReader reader)
    {
        if (reader.TokenType != YamlTokenType.Scalar)
        {
            throw YamlThrowHelper.ThrowExpectedScalar(reader);
        }

        if (!YamlScalar.TryParseInt64(reader, out var parsed) || parsed is < sbyte.MinValue or > sbyte.MaxValue)
        {
            throw YamlThrowHelper.ThrowInvalidSByteScalar(reader);
        }

        reader.Read();
        return (sbyte)parsed;
    }

    public override void Write(YamlWriter writer, sbyte value)
        => writer.WriteScalar(value.ToString(CultureInfo.InvariantCulture));
}
