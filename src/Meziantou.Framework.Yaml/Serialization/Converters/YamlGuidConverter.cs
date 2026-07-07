namespace Meziantou.Framework.Yaml.Serialization.Converters;

internal sealed class YamlGuidConverter : YamlConverter<Guid>
{
    public static YamlGuidConverter Instance { get; } = new();

    public override Guid Read(YamlReader reader)
    {
        if (reader.TokenType != YamlTokenType.Scalar)
        {
            throw YamlThrowHelper.ThrowExpectedScalar(reader);
        }

        var text = reader.ScalarValue;
        if (!Guid.TryParse(text, out var result))
        {
            throw YamlThrowHelper.ThrowInvalidGuidScalar(reader);
        }

        reader.Read();
        return result;
    }

    public override void Write(YamlWriter writer, Guid value)
    {
        writer.WriteScalar(value);
    }
}
