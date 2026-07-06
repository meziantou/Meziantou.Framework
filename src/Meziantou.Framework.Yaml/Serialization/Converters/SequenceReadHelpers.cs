namespace Meziantou.Framework.Yaml.Serialization.Converters;

internal static class SequenceReadHelpers
{
    public static List<TElement>? ReadList<TElement>(YamlReader reader, ref YamlConverter? elementConverter, string typeDisplayName)
    {
        if (reader.TryReadAlias(out var rootAliasValue))
        {
            return (List<TElement>)rootAliasValue!;
        }

        if (reader.TokenType == YamlTokenType.Alias)
        {
            throw new YamlException(reader.SourceName, reader.Start, reader.End, $"Aliases are not supported when deserializing into {typeDisplayName} unless ReferenceHandling is Preserve.");
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

        elementConverter ??= reader.GetConverter(typeof(TElement));
        var anchor = reader.Anchor;
        reader.Read();

        var list = new List<TElement>();
        if (reader.ReferenceReader is not null && anchor is not null)
        {
            reader.ReferenceReader.Register(anchor, list);
        }

        while (reader.TokenType != YamlTokenType.EndSequence)
        {
            var value = elementConverter.Read(reader, typeof(TElement));
            list.Add((TElement)value!);
        }

        reader.Read();
        return list;
    }

    public static void WriteEnumerable<TElement>(YamlWriter writer, IEnumerable<TElement>? value, ref YamlConverter? elementConverter)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        elementConverter ??= writer.GetConverter(typeof(TElement));

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
        foreach (var item in value)
        {
            elementConverter.Write(writer, item);
        }
        writer.WriteEndSequence();
    }

    public static ICollection<TElement>? PopulateCollection<TElement>(YamlReader reader, ICollection<TElement> collection, ref YamlConverter? elementConverter, string typeDisplayName)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(collection);

        if (reader.TryReadAlias(out var rootAliasValue))
        {
            return (ICollection<TElement>)rootAliasValue!;
        }

        if (reader.TokenType == YamlTokenType.Alias)
        {
            throw new YamlException(reader.SourceName, reader.Start, reader.End, $"Aliases are not supported when deserializing into {typeDisplayName} unless ReferenceHandling is Preserve.");
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
            reader.ReferenceReader.Register(reader.Anchor, collection);
        }

        elementConverter ??= reader.GetConverter(typeof(TElement));
        reader.Read();

        while (reader.TokenType != YamlTokenType.EndSequence)
        {
            var value = elementConverter.Read(reader, typeof(TElement));
            collection.Add((TElement)value!);
        }

        reader.Read();
        return collection;
    }
}
