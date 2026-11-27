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

        if (!YamlScalar.TryParseDateTimeOffset(reader, out var result))
        {
            throw YamlThrowHelper.ThrowInvalidDateTimeOffsetScalar(reader);
        }

        reader.Read();
        return result;
    }

    public override void Write(YamlWriter writer, DateTimeOffset value)
    {
        writer.WriteScalar(value);
    }
}
