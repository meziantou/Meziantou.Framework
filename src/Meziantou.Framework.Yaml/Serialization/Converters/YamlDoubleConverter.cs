namespace Meziantou.Framework.Yaml.Serialization.Converters;

internal sealed class YamlDoubleConverter : YamlConverter<double>
{
    public static YamlDoubleConverter Instance { get; } = new();

    public override double Read(YamlReader reader)
    {
        if (reader.TokenType != YamlTokenType.Scalar)
        {
            throw YamlThrowHelper.ThrowExpectedScalar(reader);
        }

        if (!YamlScalar.TryParseDouble(reader, out var result))
        {
            throw YamlThrowHelper.ThrowInvalidFloatScalar(reader);
        }

        reader.Read();
        return result;
    }

    public override void Write(YamlWriter writer, double value)
    {
        writer.WriteScalar(value);
    }
}
