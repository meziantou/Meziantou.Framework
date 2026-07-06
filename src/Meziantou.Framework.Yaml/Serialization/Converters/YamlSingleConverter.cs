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
        if (float.IsPositiveInfinity(value))
        {
            writer.WriteScalar(".inf");
            return;
        }

        if (float.IsNegativeInfinity(value))
        {
            writer.WriteScalar("-.inf");
            return;
        }

        if (float.IsNaN(value))
        {
            writer.WriteScalar(".nan");
            return;
        }

        writer.WriteScalar(value.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
    }
}
