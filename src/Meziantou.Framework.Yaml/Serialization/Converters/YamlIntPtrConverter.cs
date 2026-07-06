namespace Meziantou.Framework.Yaml.Serialization.Converters;

internal sealed class YamlIntPtrConverter : YamlConverter<nint>
{
    public static YamlIntPtrConverter Instance { get; } = new();

    public override nint Read(YamlReader reader)
    {
        if (reader.TokenType != YamlTokenType.Scalar)
        {
            throw YamlThrowHelper.ThrowExpectedScalar(reader);
        }

        if (!YamlScalar.TryParseInt64(reader, out var parsed))
        {
            throw YamlThrowHelper.ThrowInvalidNIntScalar(reader);
        }

        reader.Read();
        return (nint)parsed;
    }

    public override void Write(YamlWriter writer, nint value)
        => writer.WriteScalar(((long)value).ToString(CultureInfo.InvariantCulture));
}
