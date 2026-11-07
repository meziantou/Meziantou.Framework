namespace Meziantou.Framework.Yaml.Serialization.Converters;

internal sealed class YamlNullableConverter<T> : YamlConverter<T?> where T : struct
{
    public static YamlNullableConverter<T> Instance { get; } = new();

    public override T? Read(YamlReader reader)
    {
        if (reader.TokenType == YamlTokenType.Scalar && YamlScalar.IsNull(reader))
        {
            reader.Read();
            return null;
        }

        // The converter of T can be a non-generic YamlConverter, for example one registered in the options.
        var converter = reader.GetConverter(typeof(T));
        if (converter is YamlConverter<T> typedConverter)
        {
            return typedConverter.Read(reader);
        }

        return (T?)converter.Read(reader, typeof(T));
    }

    public override void Write(YamlWriter writer, T? value)
    {
        if (!value.HasValue)
        {
            writer.WriteNullValue();
            return;
        }

        var converter = writer.GetConverter(typeof(T));
        if (converter is YamlConverter<T> typedConverter)
        {
            typedConverter.Write(writer, value.GetValueOrDefault());
            return;
        }

        converter.Write(writer, value.GetValueOrDefault());
    }
}
