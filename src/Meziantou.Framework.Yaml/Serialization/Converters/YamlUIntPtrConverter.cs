namespace Meziantou.Framework.Yaml.Serialization.Converters;

internal sealed class YamlUIntPtrConverter : YamlConverter<nuint>
{
    public static YamlUIntPtrConverter Instance { get; } = new();

    public override nuint Read(YamlReader reader)
    {
        if (reader.TokenType != YamlTokenType.Scalar)
        {
            throw YamlThrowHelper.ThrowExpectedScalar(reader);
        }

        if (!YamlScalar.TryParseUInt64(reader, out var parsed))
        {
            throw YamlThrowHelper.ThrowInvalidNUIntScalar(reader);
        }

        reader.Read();
        return (nuint)parsed;
    }

    public override void Write(YamlWriter writer, nuint value)
        => writer.WriteScalar(((ulong)value).ToString(CultureInfo.InvariantCulture));
}
