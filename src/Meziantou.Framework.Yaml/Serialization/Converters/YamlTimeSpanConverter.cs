namespace Meziantou.Framework.Yaml.Serialization.Converters;

internal sealed class YamlTimeSpanConverter : YamlConverter<TimeSpan>
{
    public static YamlTimeSpanConverter Instance { get; } = new();

    public override TimeSpan Read(YamlReader reader)
    {
        if (reader.TokenType != YamlTokenType.Scalar)
        {
            throw YamlThrowHelper.ThrowExpectedScalar(reader);
        }

        var text = reader.ScalarValue;
        if (!TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out var result))
        {
            throw YamlThrowHelper.ThrowInvalidTimeSpanScalar(reader);
        }

        reader.Read();
        return result;
    }

    public override void Write(YamlWriter writer, TimeSpan value)
    {
        writer.WriteScalar(value);
    }
}
