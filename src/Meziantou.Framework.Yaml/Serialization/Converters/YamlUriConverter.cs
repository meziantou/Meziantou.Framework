namespace Meziantou.Framework.Yaml.Serialization.Converters;

internal sealed class YamlUriConverter : YamlConverter<Uri?>
{
    public static YamlUriConverter Instance { get; } = new();

    public override Uri? Read(YamlReader reader)
    {
        if (reader.TokenType != YamlTokenType.Scalar)
        {
            throw YamlThrowHelper.ThrowExpectedScalar(reader);
        }

        if (YamlScalar.IsNull(reader))
        {
            reader.Read();
            return null;
        }

        var text = reader.ScalarValue;
        if (!Uri.TryCreate(text, UriKind.RelativeOrAbsolute, out var result))
        {
            throw YamlThrowHelper.ThrowInvalidUriScalar(reader);
        }

        reader.Read();
        return result;
    }

    public override void Write(YamlWriter writer, Uri? value)
    {
        writer.WriteScalar(value?.OriginalString);
    }
}
