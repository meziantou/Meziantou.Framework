namespace Meziantou.Framework.Yaml.Serialization.Converters;

internal sealed class YamlInt16Converter : YamlConverter<short>
{
    public static YamlInt16Converter Instance { get; } = new();

    public override short Read(YamlReader reader)
    {
        if (reader.TokenType != YamlTokenType.Scalar)
        {
            throw YamlThrowHelper.ThrowExpectedScalar(reader);
        }

        if (!YamlScalar.TryParseInt64(reader, out var parsed) || parsed is < short.MinValue or > short.MaxValue)
        {
            throw YamlThrowHelper.ThrowInvalidInt16Scalar(reader);
        }

        reader.Read();
        return (short)parsed;
    }

    public override void Write(YamlWriter writer, short value)
        => writer.WriteScalar(value.ToString(CultureInfo.InvariantCulture));
}
