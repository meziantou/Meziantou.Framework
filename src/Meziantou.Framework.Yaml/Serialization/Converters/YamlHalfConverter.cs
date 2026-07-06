namespace Meziantou.Framework.Yaml.Serialization.Converters;

internal sealed class YamlHalfConverter : YamlConverter<Half>
{
    public static YamlHalfConverter Instance { get; } = new();

    public override Half Read(YamlReader reader)
    {
        if (reader.TokenType != YamlTokenType.Scalar)
        {
            throw YamlThrowHelper.ThrowExpectedScalar(reader);
        }

        var text = reader.ScalarValue;
        if (!Half.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
        {
            throw YamlThrowHelper.ThrowInvalidHalfScalar(reader);
        }

        reader.Read();
        return result;
    }

    public override void Write(YamlWriter writer, Half value)
    {
        writer.WriteScalar(value.ToString(CultureInfo.InvariantCulture));
    }
}
