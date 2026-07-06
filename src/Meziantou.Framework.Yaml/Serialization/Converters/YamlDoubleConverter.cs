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
        if (double.IsPositiveInfinity(value))
        {
            writer.WriteScalar(".inf");
            return;
        }

        if (double.IsNegativeInfinity(value))
        {
            writer.WriteScalar("-.inf");
            return;
        }

        if (double.IsNaN(value))
        {
            writer.WriteScalar(".nan");
            return;
        }

        writer.WriteScalar(value.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
    }
}
