namespace Meziantou.Framework.Yaml.Serialization.Converters;

internal sealed class YamlVersionConverter : YamlConverter<System.Version?>
{
    public static YamlVersionConverter Instance { get; } = new();

    public override System.Version? Read(YamlReader reader)
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

        if (!System.Version.TryParse(reader.ScalarValue, out var result))
        {
            throw YamlThrowHelper.ThrowInvalidVersionScalar(reader);
        }

        reader.Read();
        return result;
    }

    public override void Write(YamlWriter writer, System.Version? value)
    {
        writer.WriteScalar(value?.ToString());
    }
}
