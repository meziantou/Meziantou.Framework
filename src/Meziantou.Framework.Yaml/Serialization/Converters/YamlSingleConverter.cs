namespace Meziantou.Framework.Yaml.Serialization.Converters;

internal sealed class YamlSingleConverter : YamlConverter<float>
{
    public static YamlSingleConverter Instance { get; } = new();

    public override float Read(YamlReader reader)
    {
        if (reader.TokenType != YamlTokenType.Scalar)
        {
            throw YamlThrowHelper.ThrowExpectedScalar(reader);
        }

        if (!YamlScalar.TryParseDouble(reader, out var parsed))
        {
            throw YamlThrowHelper.ThrowInvalidFloatScalar(reader);
        }

        reader.Read();
        return (float)parsed;
    }

    public override void Write(YamlWriter writer, float value)
    {
        writer.WriteScalar(value);
    }
}
