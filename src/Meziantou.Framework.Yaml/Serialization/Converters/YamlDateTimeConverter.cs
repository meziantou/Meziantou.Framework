namespace Meziantou.Framework.Yaml.Serialization.Converters;

internal sealed class YamlDateTimeConverter : YamlConverter<DateTime>
{
    public static YamlDateTimeConverter Instance { get; } = new();

    public override DateTime Read(YamlReader reader)
    {
        if (reader.TokenType != YamlTokenType.Scalar)
        {
            throw YamlThrowHelper.ThrowExpectedScalar(reader);
        }

        var text = reader.ScalarValue;
        if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var result))
        {
            throw YamlThrowHelper.ThrowInvalidDateTimeScalar(reader);
        }

        reader.Read();
        return result;
    }

    public override void Write(YamlWriter writer, DateTime value)
    {
        writer.WriteScalar(value);
    }
}
