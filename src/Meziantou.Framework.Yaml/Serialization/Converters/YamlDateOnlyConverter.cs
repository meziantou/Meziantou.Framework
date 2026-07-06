namespace Meziantou.Framework.Yaml.Serialization.Converters;

internal sealed class YamlDateOnlyConverter : YamlConverter<DateOnly>
{
    public static YamlDateOnlyConverter Instance { get; } = new();

    public override DateOnly Read(YamlReader reader)
    {
        if (reader.TokenType != YamlTokenType.Scalar)
        {
            throw YamlThrowHelper.ThrowExpectedScalar(reader);
        }

        var text = reader.ScalarValue;
        if (!DateOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var result))
        {
            throw YamlThrowHelper.ThrowInvalidDateOnlyScalar(reader);
        }

        reader.Read();
        return result;
    }

    public override void Write(YamlWriter writer, DateOnly value)
    {
        writer.WriteScalar(value.ToString("O", CultureInfo.InvariantCulture));
    }
}
