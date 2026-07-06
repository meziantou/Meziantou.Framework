namespace Meziantou.Framework.Yaml.Serialization.Converters;

internal sealed class YamlDateTimeOffsetConverter : YamlConverter<DateTimeOffset>
{
    public static YamlDateTimeOffsetConverter Instance { get; } = new();

    public override DateTimeOffset Read(YamlReader reader)
    {
        if (reader.TokenType != YamlTokenType.Scalar)
        {
            throw YamlThrowHelper.ThrowExpectedScalar(reader);
        }

        var text = reader.ScalarValue;
        if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var result))
        {
            throw YamlThrowHelper.ThrowInvalidDateTimeOffsetScalar(reader);
        }

        reader.Read();
        return result;
    }

    public override void Write(YamlWriter writer, DateTimeOffset value)
    {
        writer.WriteScalar(value.ToString("O", CultureInfo.InvariantCulture));
    }
}
