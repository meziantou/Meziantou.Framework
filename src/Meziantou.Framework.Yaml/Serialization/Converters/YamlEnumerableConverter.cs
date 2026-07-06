namespace Meziantou.Framework.Yaml.Serialization.Converters;

internal sealed class YamlEnumerableConverter<TElement> : YamlConverter<IEnumerable<TElement>?>
{
    private YamlConverter? _elementConverter;

    public override IEnumerable<TElement>? Read(YamlReader reader)
    {
        if (reader.TryReadAlias(out var rootAliasValue))
        {
            return (IEnumerable<TElement>)rootAliasValue!;
        }

        if (reader.TokenType == YamlTokenType.Alias)
        {
            throw new YamlException(reader.SourceName, reader.Start, reader.End, "Aliases are not supported when deserializing into an enumerable unless ReferenceHandling is Preserve.");
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

        _elementConverter ??= reader.GetConverter(typeof(TElement));
        var anchor = reader.Anchor;
        reader.Read();

        var list = new List<TElement>();
        if (reader.ReferenceReader is not null && anchor is not null)
        {
            reader.ReferenceReader.Register(anchor, list);
        }

        while (reader.TokenType != YamlTokenType.EndSequence)
        {
            var value = _elementConverter.Read(reader, typeof(TElement));
            list.Add((TElement)value!);
        }

        reader.Read();
        return list;
    }

    public override void Write(YamlWriter writer, IEnumerable<TElement>? value)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        _elementConverter ??= writer.GetConverter(typeof(TElement));

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
            _elementConverter.Write(writer, item);
        }
        writer.WriteEndSequence();
    }
}
