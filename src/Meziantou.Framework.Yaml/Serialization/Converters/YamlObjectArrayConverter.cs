namespace Meziantou.Framework.Yaml.Serialization.Converters;

internal sealed class YamlObjectArrayConverter : YamlConverter<object?[]?>
{
    public static YamlObjectArrayConverter Instance { get; } = new();

    public override object?[]? Read(YamlReader reader)
    {
        if (reader.TryReadAlias(out var rootAliasValue))
        {
            return (object?[]?)rootAliasValue;
        }

        if (reader.TokenType == YamlTokenType.Alias)
        {
            throw new YamlException(reader.SourceName, reader.Start, reader.End, "Aliases are not supported when deserializing into an array unless ReferenceHandling is Preserve.");
        }

        if (reader.TokenType == YamlTokenType.Scalar && YamlScalar.IsNull(reader))
        {
            reader.Read();
            return null;
        }

        if (reader.TokenType != YamlTokenType.StartSequence)
        {
            throw YamlThrowHelper.ThrowExpectedSequence(reader);
        }

        var anchor = reader.Anchor;
        reader.Read();

        var list = new List<object?>();
        while (reader.TokenType != YamlTokenType.EndSequence)
        {
            var value = reader.GetConverter(typeof(object)).Read(reader, typeof(object));
            list.Add(value);
        }

        reader.Read();
        var array = list.ToArray();
        if (reader.ReferenceReader is not null && anchor is not null)
        {
            reader.ReferenceReader.Register(anchor, array);
        }

        return array;
    }

    public override void Write(YamlWriter writer, object?[]? value)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        if (writer.ReferenceWriter is not null)
        {
            if (writer.ReferenceWriter.TryGetAnchor(value, out var existing))
            {
                writer.WriteAlias(existing);
                return;
            }

            var anchor = writer.ReferenceWriter.GetOrAddAnchor(value);
            writer.WriteAnchor(anchor);
        }

        writer.WriteStartSequence();
        for (var i = 0; i < value.Length; i++)
        {
            writer.GetConverter(typeof(object)).Write(writer, value[i]);
        }
        writer.WriteEndSequence();
    }
}
