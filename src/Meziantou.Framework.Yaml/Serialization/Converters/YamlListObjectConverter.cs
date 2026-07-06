namespace Meziantou.Framework.Yaml.Serialization.Converters;

internal sealed class YamlListObjectConverter : YamlConverter<List<object?>?>
{
    public static YamlListObjectConverter Instance { get; } = new();

    public override bool CanPopulate(Type typeToConvert) => typeToConvert == typeof(List<object>);

    public override object? Populate(YamlReader reader, Type typeToConvert, object existingValue)
    {
        ArgumentNullException.ThrowIfNull(existingValue);
        if (existingValue is not List<object?> list)
        {
            throw new InvalidOperationException($"Existing value for '{typeToConvert}' must be a '{typeof(List<object>)}'.");
        }

        return PopulateList(reader, list);
    }

    public override List<object?>? Read(YamlReader reader)
    {
        if (reader.TryReadAlias(out var rootAliasValue))
        {
            return (List<object?>)rootAliasValue!;
        }

        if (reader.TokenType == YamlTokenType.Alias)
        {
            throw new YamlException(reader.SourceName, reader.Start, reader.End, "Aliases are not supported when deserializing into a list unless ReferenceHandling is Preserve.");
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
        if (reader.ReferenceReader is not null && anchor is not null)
        {
            reader.ReferenceReader.Register(anchor, list);
        }

        while (reader.TokenType != YamlTokenType.EndSequence)
        {
            var value = reader.GetConverter(typeof(object)).Read(reader, typeof(object));
            list.Add(value);
        }

        reader.Read();
        return list;
    }

    public override void Write(YamlWriter writer, List<object?>? value)
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
        for (var i = 0; i < value.Count; i++)
        {
            writer.GetConverter(typeof(object)).Write(writer, value[i]);
        }
        writer.WriteEndSequence();
    }

    private static List<object?>? PopulateList(YamlReader reader, List<object?> list)
    {
        if (reader.TryReadAlias(out var rootAliasValue))
        {
            return (List<object?>)rootAliasValue!;
        }

        if (reader.TokenType == YamlTokenType.Alias)
        {
            throw new YamlException(reader.SourceName, reader.Start, reader.End, "Aliases are not supported when deserializing into a list unless ReferenceHandling is Preserve.");
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

        if (reader.ReferenceReader is not null && reader.Anchor is not null)
        {
            reader.ReferenceReader.Register(reader.Anchor, list);
        }

        reader.Read();
        while (reader.TokenType != YamlTokenType.EndSequence)
        {
            var value = reader.GetConverter(typeof(object)).Read(reader, typeof(object));
            list.Add(value);
        }

        reader.Read();
        return list;
    }
}
