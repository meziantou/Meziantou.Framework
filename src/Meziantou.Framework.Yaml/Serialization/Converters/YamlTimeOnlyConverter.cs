namespace Meziantou.Framework.Yaml.Serialization.Converters;

internal sealed class YamlTimeOnlyConverter : YamlConverter<TimeOnly>
{
    public static YamlTimeOnlyConverter Instance { get; } = new();

    public override TimeOnly Read(YamlReader reader)
    {
        if (reader.TokenType != YamlTokenType.Scalar)
        {
            throw YamlThrowHelper.ThrowExpectedScalar(reader);
        }

        var text = reader.ScalarValue;
        if (!TimeOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var result))
        {
            throw YamlThrowHelper.ThrowInvalidTimeOnlyScalar(reader);
        }

        reader.Read();
        return result;
    }

    public override void Write(YamlWriter writer, TimeOnly value)
    {
        writer.WriteScalar(value);
    }
}
